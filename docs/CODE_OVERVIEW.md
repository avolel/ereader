# EReader — Code Overview

A deep-dive walk-through of the EReader codebase: what each piece does, why it's
structured the way it is, and how the parts fit together. Read it top-to-bottom
as a new contributor, or jump to a section as a per-module reference.

> The [README](../README.md) is the front door — start there for setup, the API
> surface, and the high-level map. This document goes deeper. Sections 3–11 are
> the canonical reference for the **backend internals** (the auth token model,
> the ingestion pipeline, EF mapping decisions). **Section 12 is a full frontend
> deep-dive written for engineers new to React Native / Expo** — if that's you,
> you can read §12 on its own.

---

## 1. Architecture at a glance

EReader is a personal ebook-library application. The user uploads `.epub`
files, the backend parses them into chapters + assets, and the client reads
them chapter-by-chapter. The codebase splits cleanly along three boundaries:

```
┌──────────────────────┐   HTTPS / JSON    ┌────────────────────────────┐
│  frontend (Expo RN)  │ ───────────────▶ │  backend (ASP.NET Core 10) │
│  React Native + Web  │                   │  EReader.Api               │
└──────────────────────┘                   │  EReader.Core              │
                                           │  EReader.Data              │
                                           └────────────┬───────────────┘
                                                        │
                                          ┌─────────────┴─────────────┐
                                          ▼                           ▼
                                  ┌──────────────┐            ┌────────────┐
                                  │ PostgreSQL   │            │   Redis    │
                                  │ (EF Core)    │            │  (refresh  │
                                  └──────────────┘            │   tokens)  │
                                                              └────────────┘
                                                +
                                          ┌──────────────────────────┐
                                          │ Local filesystem store   │
                                          │ (../data/books/{bookId}) │
                                          └──────────────────────────┘
```

### Backend split — three projects

| Project | Role |
|---|---|
| `EReader.Api` | HTTP boundary. Controllers, DTOs, middleware, `Program.cs` wiring, auth integration with `HttpContext`. |
| `EReader.Core` | Pure domain. Models, service interfaces, domain exceptions, business logic in `Services/`. **No I/O, no HTTP, no EF.** |
| `EReader.Data` | Adapters for everything `Core` abstracts: EF `DbContext`, repositories, EPUB parser/asset reader, BCrypt hasher, JWT issuer, Redis store, local file store. |

The boundary rule: `Core` only references BCL types; `Data` may reference
`Core`; `Api` may reference both. `Core` defines the *interfaces*, `Data`
provides the *implementations*, `Api` wires them up via DI. That's what
enables the unit tests to run without Postgres/Redis at all — every external
dependency has an interface they can fake.

### Frontend split

The Expo app is a full React Native + Web client: register/login, a paginated
library grid, an EPUB reader backed by a WebView, full-text search, and a
reading-settings drawer. It uses `expo-router` for file-based routing, TanStack
React Query for server state, and React Context for auth + theme. The structure
follows the project standard: `app/` (routes), `src/screens/`, `src/components/`,
`src/hooks/`, `src/providers/`, `src/services/`, `src/types/`. The full
walk-through is in [§12](#12-frontend-deep-dive-for-engineers-new-to-react-native).

---

## 2. Project layout

```
ereader/
├── backend/
│   ├── EReader.Api/        ← HTTP layer
│   │   ├── Auth/           ← JWT bearer setup, Swagger auth, current-user accessor
│   │   ├── Controllers/    ← AuthController, UsersController, BooksController
│   │   ├── Dtos/           ← Request + response shapes
│   │   ├── Middleware/     ← Global error handler
│   │   ├── Program.cs      ← Composition root
│   │   └── appsettings*.json
│   ├── EReader.Core/       ← Pure domain
│   │   ├── Auth/           ← AuthTokens, IssuedRefreshToken, ConsumedRefreshToken
│   │   ├── Books/          ← BookAsset, BookListPage, BookWithChapters,
│   │   │                     ChapterContent, ParsedEpub
│   │   ├── Exceptions/     ← Domain exceptions mapped to HTTP by middleware
│   │   ├── Interfaces/     ← All service + repository + adapter contracts
│   │   ├── Models/         ← User, Book, Chapter, Annotation, Bookmark,
│   │   │                     ReadingSetting, AnnotationType
│   │   └── Services/       ← AuthService, UserService, BookService,
│   │                         BookIngestionService, CredentialValidator,
│   │                         AssetUrlRewriter
│   ├── EReader.Data/       ← Adapters
│   │   ├── Auth/           ← BCryptPasswordHasher, JwtTokenIssuer,
│   │   │                     JwtOptions, RedisOptions, RedisRefreshTokenStore
│   │   ├── Migrations/     ← EF Core migrations
│   │   ├── Parsing/        ← EpubParserAdapter, ZipEpubAssetReader
│   │   ├── Repositories/   ← UserRepository, BookRepository
│   │   ├── Storage/        ← LocalBookFileStore, BookStorageOptions
│   │   ├── EReaderDbContext.cs
│   │   └── DesignTimeDbContextFactory.cs
│   └── EReader.Tests/      ← xUnit + FluentAssertions + Moq + EF InMemory
├── frontend/               ← Expo (React Native + Web)
│   ├── app/                ← expo-router file routes (+ (authed) group)
│   └── src/
│       ├── screens/        ← Library, Reader, Search, Login, Register
│       ├── components/     ← ReaderWebView(.web), SettingsDrawer, TOC, AuthImage
│       ├── hooks/          ← React Query data-fetching hooks
│       ├── providers/      ← Query / Auth / Theme context
│       ├── services/       ← axios client + endpoint wrappers + tokenStorage
│       ├── lib/, theme/    ← WebView document builder, color tokens
│       └── types/index.ts
├── docker/                 ← docker-compose with Postgres 16 + Redis 7
├── docs/                   ← This file lives here
├── scripts/                ← download-test-books.sh
├── test-books/             ← Sample EPUBs for local testing
├── best-practices.md       ← Coding standards (READ FIRST when touching code)
└── CLAUDE.md               ← Project-level agent instructions
```

---

## 3. Runtime composition — [Program.cs](../backend/EReader.Api/Program.cs)

`Program.cs` is the composition root — the only place where concrete types
get matched to interfaces. Walk through it linearly:

1. **`.env` load (Development only).** `DotNetEnv.Env.TraversePath().Load()`
   walks up from the running directory looking for a `.env`, so
   `DATABASE_URL`, `JWT__KEY`, and `REDIS__CONNECTIONSTRING` get into
   `IConfiguration`. In production a `.env` must not exist; the same vars
   are set as real environment variables.

2. **Auth-by-default filter.** Every controller endpoint requires an
   authenticated user unless it carries `[AllowAnonymous]`. The fallback
   policy is added via `AuthorizeFilter`:

   ```csharp
   builder.Services.AddControllers(opts => {
       var policy = new AuthorizationPolicyBuilder()
           .RequireAuthenticatedUser().Build();
       opts.Filters.Add(new AuthorizeFilter(policy));
   });
   ```

   This is the inversion of "opt-in `[Authorize]`" — it's
   "opt-*out* via `[AllowAnonymous]`". Currently only the three auth-flow
   endpoints (`register`, `login`, `refresh`) opt out.

3. **OpenAPI / Swagger.** `AddSwaggerWithAuth()` registers a Bearer security
   scheme so Swagger UI prompts for the JWT and applies it to every
   operation. Setup lives in [SwaggerAuthSetup.cs](../backend/EReader.Api/Auth/SwaggerAuthSetup.cs).

4. **DbContext.** `DATABASE_URL` (from the env) is preferred over the named
   connection string. `UseNpgsql(...)` plugs in the PostgreSQL provider from
   `Npgsql.EntityFrameworkCore.PostgreSQL`.

5. **Redis multiplexer.** `IConnectionMultiplexer` is registered as a
   singleton, which is the StackExchange.Redis recommendation — the
   multiplexer pools connections and is fully thread-safe. The
   `RedisOptions` binding is `ValidateOnStart`, so a missing
   `Redis:ConnectionString` fails fast at boot, not on the first refresh.

6. **JWT auth.** [JwtBearerSetup.AddEreaderAuth](../backend/EReader.Api/Auth/JwtBearerSetup.cs)
   binds `JwtOptions`, validates that the key is at least 32 bytes UTF-8
   (HS256 minimum), wires `JwtBearer` with `ValidateIssuer/Audience/Lifetime`
   all on, and adds 30s of clock skew tolerance.

7. **Auth service graph (DI lifetimes are deliberate):**
   - `IPasswordHasher → BCryptPasswordHasher` — **singleton** (stateless).
   - `IJwtTokenIssuer → JwtTokenIssuer` — **singleton** (holds prebuilt
     `SigningCredentials`).
   - `IRefreshTokenStore → RedisRefreshTokenStore` — **singleton** (its only
     deps are the singleton multiplexer + options).
   - `IUserRepository`, `IAuthService`, `IUserService` — **scoped** (touch
     the DbContext, which is scoped).

8. **Current-user accessor.** `AddHttpContextAccessor()` is required so that
   `HttpContextCurrentUserService` (scoped) can pull the JWT claims out of
   the current request.

9. **Book ingestion + reader services:**
   - `BookStorageOptions` is bound but **not** `ValidateOnStart` — the
     default `../data/books` works out of the box.
   - `IBookFileStore → LocalBookFileStore` — **singleton** (just holds a
     resolved root path).
   - `IEpubParser → EpubParserAdapter` and `IEpubAssetReader → ZipEpubAssetReader`
     — **transient** (stateless wrappers around VersOne.Epub / ZipArchive).
   - `IBookRepository`, `IBookService`, `IBookIngestionService` — **scoped**.

10. **Pipeline order:**
    `UseMiddleware<ErrorHandlingMiddleware>()` →
    (dev: auto-migrate + Swagger) →
    `UseAuthentication()` → `UseAuthorization()` →
    `MapControllers()` → root redirect to `/swagger`.

    The error middleware sits **outermost** on purpose — it catches
    exceptions thrown anywhere downstream, including from auth.

11. **Dev-only auto-migrate.** In Development, `db.Database.MigrateAsync()`
    runs on boot so a freshly-pulled branch with new migrations doesn't 500
    the first request. Production migrations are explicitly *not* auto-run
    here — they go through a deliberate deployment step.

---

## 4. Authentication & authorisation

This is the most security-sensitive subsystem in the code, so it gets the
deepest treatment.

### 4.1 Token model

There are **two distinct credentials** the API issues:

| Credential | Format | Storage | Lifetime |
|---|---|---|---|
| Access token | JWT (HS256) with `sub`, `preferred_username`, `jti`, `fid` claims | Stateless, validated cryptographically each request | `Jwt:AccessTokenMinutes` (default 15) |
| Refresh token | 32-byte CSPRNG, base64url-encoded | SHA-256 hash + metadata in Redis | `Redis:RefreshTokenDays` (default 30) |

Refresh tokens are **opaque** — they aren't JWTs and carry no information.
All meaningful state lives in Redis under three key shapes:

- `ereader:refresh:token:{sha256(token)}` — hash with `userId`, `familyId`,
  `expiresAt`, `revoked`.
- `ereader:refresh:family:{familyId}` — Redis set of all token hashes in
  the family.
- `ereader:refresh:user:{userId}` — Redis set of all family ids for the user.

These three shapes give us O(1) revocation at three granularities
(token / family / user) without scanning the keyspace.

### 4.2 The "family" concept

A *family* is the chain of refresh tokens that descend from a single login.
Every login creates a new family. Every refresh rotates *within* the family
(consume token N, issue token N+1, both share `familyId`). This is the
classic refresh-token-rotation pattern, and the reason for it is **reuse
detection**:

If a token that has already been consumed is presented again, that's either
(a) a buggy client double-submitting or (b) an attacker who stole the
token and tried to use it. We can't tell, so we treat it as theft:
[RedisRefreshTokenStore.ValidateAndConsumeAsync](../backend/EReader.Data/Auth/RedisRefreshTokenStore.cs)
sees `revoked=1`, reads the `familyId`, and calls `RevokeFamilyAsync` —
**every** sibling token in that login chain is invalidated immediately. The
caller gets `REFRESH_REUSED` back.

The `familyId` is also stamped into the access token as the `fid` claim
([JwtTokenIssuer.cs](../backend/EReader.Data/Auth/JwtTokenIssuer.cs)), which
is what powers `revokeOtherSessions: true` on change-password — the current
session's family is preserved, every other family for that user gets killed.

### 4.3 Atomic consume — the Lua script

The most subtle piece of the refresh flow is `ConsumeScript` in
[RedisRefreshTokenStore.cs:125](../backend/EReader.Data/Auth/RedisRefreshTokenStore.cs#L125):

```lua
local row = redis.call('HMGET', KEYS[1], 'userId', 'familyId', 'revoked')
if (not row[1]) then return {0} end
if (row[3] == '1') then return {2, row[1], row[2]} end
redis.call('HSET', KEYS[1], 'revoked', '1')
return {1, row[1], row[2]}
```

It's read-then-write in **one round trip**, so two concurrent refresh
attempts with the same token can't both succeed and can't both pass the
"is it revoked?" check. Status codes: `0` = not found, `1` = consumed
successfully, `2` = reuse detected (caller revokes the family).

### 4.4 Hashing — why SHA-256, not BCrypt, on refresh tokens

Refresh tokens are 32 bytes of CSPRNG output — they are already
uniformly random and have ~256 bits of entropy. The threat model is
"someone exfiltrates the Redis dump"; we hash the *token* before storing
so the dump can't be replayed against the live API. SHA-256 is sufficient
here because there's no dictionary attack to slow down (every input is
random). BCrypt is reserved for the **password** flow, where slowing down
guesses matters.

### 4.5 The flows end-to-end

**Register** ([AuthService.RegisterAsync](../backend/EReader.Core/Services/AuthService.cs)):
1. Validate username + password via [CredentialValidator](../backend/EReader.Core/Services/CredentialValidator.cs)
   (3–32 chars, `[a-zA-Z0-9_-]`, password ≥ 10 chars with letter + digit).
2. Check uniqueness (case-insensitively).
3. Hash password with BCrypt (work factor 12 ≈ 250ms).
4. Insert `User`, issue new session (access + refresh, fresh family).
5. Controller returns `201 Created` with a `Location: /api/v1/users/me`
   header (via `CreatedAtAction`) and the token payload.

**Login**:
1. Look up by lower-cased username.
2. **Constant-message failure** for both "no such user" and "wrong password"
   so the API doesn't leak which usernames exist:
   ```csharp
   if (user is null || !_hasher.Verify(password, user.PasswordHash))
       throw new AuthenticationException("INVALID_CREDENTIALS", ...);
   ```
3. Reject inactive users with the *same* message.
4. Update `LastLoginAt`, issue new session.

**Refresh**:
1. `ValidateAndConsumeAsync` atomically consumes the token (or detects
   reuse → family kill).
2. Re-fetch user; if disabled, kill the family and 401.
3. Issue rotated session: new access token + new refresh token within the
   **same familyId**.

**Logout** — revokes the supplied refresh token (`revoked=1`, keep row
until natural TTL so reuse detection still works).

**Logout-all** — sweep every family for the user, revoke every token in
each family.

**Change password** ([UserService.ChangePasswordAsync](../backend/EReader.Core/Services/UserService.cs)):
1. Verify current password.
2. Re-hash and persist the new one.
3. If `revokeOtherSessions: true`, call `RevokeOtherFamiliesAsync(userId,
   currentFamilyId)` — every family except the one belonging to the current
   token is killed. If for some reason there's no family on the current
   token, fall back to revoking everything (safer than leaving stale
   sessions alive).

### 4.6 Reading the user out of `HttpContext`

[HttpContextCurrentUserService](../backend/EReader.Api/Auth/HttpContextCurrentUserService.cs)
is a tiny adapter: it pulls `ClaimTypes.NameIdentifier` (which the JWT
handler maps from `sub`) and the custom `fid` claim. Controllers depend
on `ICurrentUserService` rather than touching `HttpContext` directly, so
the service layer never sees ASP.NET types.

### 4.7 JWT issuance & validation

- **Issuance**: HS256 with the configured key, claims = `sub`,
  `preferred_username`, `jti` (fresh GUID per token, lets us reason about
  individual access tokens in logs later), `fid`.
- **Validation**: `ValidateIssuer`, `ValidateAudience`,
  `ValidateIssuerSigningKey`, `ValidateLifetime` all on; `ClockSkew = 30s`.
- The reading side of `JwtSecurityTokenHandler` automatically remaps `sub`
  → `ClaimTypes.NameIdentifier`, which is exactly what
  `HttpContextCurrentUserService` reads — that's why the issuer code
  doesn't have to set the long-form `ClaimTypes.NameIdentifier` URI
  explicitly.

---

## 5. The book-ingestion pipeline

End-to-end, what happens when a user `POST`s an EPUB to `/api/v1/books`:

```
HTTP multipart upload
        │
        ▼
BooksController.Upload  ← validates file is present, reads userId from JWT
        │
        ▼
IBookIngestionService.IngestAsync
        │
        ├─ buffer to MemoryStream (need to hash + persist)
        ├─ SHA-256 the bytes
        ├─ ExistsByHashAsync? → ConflictException("DUPLICATE_BOOK", 409)
        ├─ LocalBookFileStore.SaveSourceAsync → ../data/books/{guid}/source.epub
        ├─ EpubParserAdapter.ParseAsync (VersOne.Epub)
        │       ↓
        │   ParsedEpub { metadata, cover, chapters[] }
        ├─ if cover → SaveCoverAsync → ../data/books/{guid}/cover.{ext}
        ├─ BookRepository.AddAsync(book, chapters) ← single SaveChanges, atomic
        │
        ▼  (on any exception above SaveChanges)
   _files.DeleteForBook(bookId)  ← clean up orphaned files
```

### 5.1 Why buffer to memory

The upload stream from `IFormFile.OpenReadStream()` is forward-only. The
ingestion code needs to read it **twice**: once to compute the SHA-256,
once to persist to disk. Reading to a `MemoryStream` lets us rewind. This
is fine for Phase 1 EPUBs (a few MB) but is flagged in a comment as worth
revisiting if larger files ever arrive.

### 5.2 Duplicate detection

The hash check is **per-user**, not global, because two different users may
legitimately have the same EPUB. The DB also has a unique index on
`(UserId, FileHash)` as a belt-and-braces, so even a race between two
concurrent uploads can't bypass the application-level check.

### 5.3 Orphan cleanup

Files are written to disk *before* the DB row is inserted (`AddAsync` is
the very last step in the `try`). If the parser blows up or the EF write
fails, the file would be orphaned — so the `catch` block calls
`DeleteForBook(bookId)` to wipe the per-book directory. The DB row was
never committed, so there's nothing else to roll back.

### 5.4 EPUB parsing — [EpubParserAdapter](../backend/EReader.Data/Parsing/EpubParserAdapter.cs)

Wraps `VersOne.Epub.EpubReader` and produces a `ParsedEpub` DTO with
title/author/language/publisher/date/year/description, the cover bytes +
extension, and a list of `ParsedChapter` rows in spine order.

Notable choices:
- **All parser exceptions are caught and re-thrown as
  `MalformedEpubException`** (mapped to HTTP 422). VersOne throws a variety
  of internal exceptions on malformed OPF/broken XML; we don't want any of
  that leaking through.
- **`OperationCanceledException` is preserved** (excluded from the catch),
  so cancellation propagates correctly.
- **Title resolution** walks the EPUB nav tree to build a
  `chapter-file-path → human title` map, keeping the first occurrence per
  file (chapter sub-headings in nav often point at the same file with
  `#anchor` suffixes; we only want the top-level title for each file).
- **`PublishedDate` is kept as a string** because OPF dates are
  notoriously inconsistent (year only, partial dates, "circa", etc.). A
  separate `PublishedYear` int is regex-extracted (`\d{4}`) for sorting
  and filtering.
- **`ExtractBody`** slices the inner HTML of `<body>...</body>` out of
  each chapter's XHTML. The client renders this fragment inside its own
  host element, so the outer `<html>`/`<head>` would just be noise.

### 5.5 Asset reading — [ZipEpubAssetReader](../backend/EReader.Data/Parsing/ZipEpubAssetReader.cs)

EPUBs are zip files. Rather than extract every image/css/font to disk on
ingest, we **stream them on demand** from the zip archive.

The tricky bit: `ZipArchive.Entry.Open()` returns a stream that depends on
the archive staying open. If we returned just the entry stream and the
caller disposed it, the archive would leak. So we wrap them in
`ArchiveOwningStream` — disposing the wrapper disposes both the entry
*and* the archive.

Lookup is case-insensitive because manifest paths inside EPUBs frequently
disagree with the zip-entry casing.

### 5.6 Asset URL rewriting — [AssetUrlRewriter](../backend/EReader.Core/Services/AssetUrlRewriter.cs)

A chapter's stored `ContentText` keeps relative refs like
`<img src="../Images/fig1.png">`. When we serve the chapter, we rewrite
those into absolute API URLs so the client doesn't need to know the
EPUB's internal layout:

```
Stored:    <img src="../Images/fig1.png" />        (chapter at OEBPS/Text/ch1.xhtml)
Served:    <img src="/api/v1/books/{id}/assets/OEBPS/Images/fig1.png" />
```

Rewriting happens at *read* time, not at *ingest* time, so the DB stays
decoupled from the API's base URL (changing the route prefix doesn't
require a backfill).

Carve-outs that are deliberately **not** rewritten:
- Absolute URLs (`http://...`)
- Fragment-only links (`#anchor`)
- Protocol-relative (`//cdn.example.com/x`)
- `data:` URIs
- Anything with a URL scheme matching `^[a-zA-Z][a-zA-Z0-9+.\-]*:`

Path resolution is full POSIX-style: `..` pops a segment off the stack,
`.` is dropped, and fragment/query (`#`, `?`) are preserved on the tail.

---

## 6. Reading a book — the API surface

[BooksController](../backend/EReader.Api/Controllers/BooksController.cs)
is the read side. Every action pulls the current user id from
`ICurrentUserService` and passes it through to the service layer — so
ownership is enforced one layer down, in `BookService` / `BookRepository`,
not in the controller.

| Verb | Route | Purpose |
|---|---|---|
| POST | `/api/v1/books` | Upload EPUB (multipart, 100 MB cap) |
| GET | `/api/v1/books` | List user's library (cursor-paginated) |
| GET | `/api/v1/books/{id}` | Book detail + table of contents |
| GET | `/api/v1/books/{id}/chapters/{chapterId}` | Chapter content (asset URLs rewritten) + prev/next IDs |
| GET | `/api/v1/books/{id}/assets/{*path}` | Stream an embedded image/css/font |
| GET | `/api/v1/books/{id}/cover` | Stream the extracted cover image |
| DELETE | `/api/v1/books/{id}` | Hard-delete book + cascade chapters + delete files |

### 6.1 Cursor pagination

Implemented in [BookService.EncodeCursor / DecodeCursor](../backend/EReader.Core/Services/BookService.cs):
the cursor is `base64("{ImportedAt-ticks}:{book-guid}")`, opaque to clients.

In the repository, the query is `WHERE ImportedAt < @cursor ORDER BY
ImportedAt DESC LIMIT pageSize + 1`. Fetching `pageSize + 1` rows is a
trick to know if there's a next page without a separate `COUNT(*)`. The
extra row is dropped before returning, and its existence flips the
`hasMore` boolean.

Page sizes: default 20, max 100, clamped in the controller's
`NormalizePageSize`.

Malformed cursors raise `ValidationException` (HTTP 400) — surfacing the
bug loudly is better than silently restarting from page 1 and looping
forever.

### 6.2 Chapter prev/next

[GetChapterAsync](../backend/EReader.Core/Services/BookService.cs) loads the
spine-ordered list of chapter IDs in a single round trip (just IDs, not
full rows — `GetChapterIdsInSpineOrderAsync`), finds the current index,
and returns the neighbouring IDs (or `null` at the boundaries). The
client doesn't have to ship the full TOC just to navigate.

### 6.3 404 vs 403

Throughout the read path, "book exists but belongs to someone else" returns
**404, not 403**. This is the standard "don't leak resource existence"
pattern — it makes the API behave identically whether the resource is
truly absent or just not yours.

### 6.4 Streaming responses

`File(asset.Content, asset.ContentType, asset.FileName)` is used for
assets and covers. ASP.NET takes ownership of the stream and disposes it
after the response is flushed — which in the asset case will cascade
through `ArchiveOwningStream` and close the underlying `ZipArchive`.

---

## 7. Users API — [UsersController](../backend/EReader.Api/Controllers/UsersController.cs)

| Verb | Route | Notes |
|---|---|---|
| GET | `/api/v1/users/me` | Current profile (named `Me` so the auth controller's `CreatedAtAction` can resolve the Location header). |
| PATCH | `/api/v1/users/me` | Rename. If `Username` is null in the request body, returns 204 without hitting the DB. |
| POST | `/api/v1/users/me/change-password` | Action-style endpoint (per the auth carve-out in best-practices). |

`change-password` accepts a `RevokeOtherSessions` flag that triggers the
"kill every family except the current one" branch in
[UserService](../backend/EReader.Core/Services/UserService.cs).

---

## 8. Error model

Every 4xx/5xx response uses the same JSON shape (enforced by best-practices
and by the error middleware):

```json
{ "error": { "code": "RESOURCE_NOT_FOUND", "message": "...", "details": null } }
```

### 8.1 Exception → status map

[ErrorHandlingMiddleware](../backend/EReader.Api/Middleware/ErrorHandlingMiddleware.cs):

| Exception type | HTTP | Code |
|---|---|---|
| `ValidationException` | 400 | `VALIDATION_ERROR` (+ optional `details` dictionary) |
| `AuthenticationException` | 401 | `INVALID_CREDENTIALS` / `REFRESH_INVALID` / `REFRESH_REUSED` / `NO_USER` |
| `AuthorizationException` | 403 | `FORBIDDEN` |
| `NotFoundException` | 404 | `RESOURCE_NOT_FOUND` |
| `ConflictException` | 409 | `RESOURCE_CONFLICT` / `USERNAME_TAKEN` / `DUPLICATE_BOOK` |
| `UnsupportedFileException` | 415 | `UNSUPPORTED_MEDIA_TYPE` |
| `MalformedEpubException` | 422 | `EPUB_MALFORMED` |
| Anything else | 500 | `INTERNAL_ERROR` (logged at `Error`) |

The middleware writes camelCase JSON and refuses to rewrite a response
that's already started flushing (the rare "exception thrown mid-streaming"
case).

### 8.2 Why exceptions, not result types

Two reasons: it lets controllers stay one-liners (`return Ok(...)` is the
only happy-path; everything else propagates), and it keeps the domain
exceptions reusable across services without forcing every caller to
translate them. The trade-off — exceptions aren't free — is acceptable
here because they're thrown on error paths only.

---

## 9. Persistence

### 9.1 [EReaderDbContext](../backend/EReader.Data/EReaderDbContext.cs)

Six entities: `User`, `Book`, `Chapter`, `Annotation`, `Bookmark`,
`ReadingSetting`. The model uses the primary-constructor form
(`class EReaderDbContext(DbContextOptions<EReaderDbContext> options) :
DbContext(options)`).

Key configuration decisions:

- **Users.Username** is `varchar(32)` with a *functional* unique index on
  `LOWER(username)`. EF can't model functional indexes declaratively, so
  `OnModelCreating` declares a regular unique index with the name
  `IX_Users_Username_Lower`; the migration is hand-edited to replace EF's
  `CreateIndex` call with `CREATE UNIQUE INDEX ... LOWER(...)`. The
  snapshot stays consistent because both forms share the same index name.
  **Future migrations that touch `Users.Username` must hand-edit the
  generated `CreateIndex`** or the case-insensitive uniqueness silently
  regresses to case-sensitive (see best-practices §"Functional Indexes").

- **Book → User** is `OnDelete(Restrict)`. Deleting a user does **not**
  silently wipe their library. The service layer has to make an explicit
  call — hard-delete the library first, or (future) soft-delete /
  anonymize the user and leave their owned rows intact. This keeps the
  soft-delete door open without DB rework.

- **Annotation.ChapterId** is nullable with `OnDelete(SetNull)` (and same
  for `Bookmark.ChapterId`). A chapter can be regenerated on EPUB
  re-import without dropping the user's annotations — `TextAnchor` is the
  durable identity; `ChapterId` is re-resolved at read time when null.

- **Annotation/Bookmark/ReadingSetting → User** are all `Restrict` for the
  same soft-delete reason as Book. **Book → Annotations** stays `Cascade`
  so deleting a book still cleans up normally.

- **Chapter** has a unique `(BookId, SpineOrder)` index. Two chapters in
  the same book can't share a position.

- **ReadingSetting** has a unique `(UserId, BookId)` index. One global
  default per user (`BookId NULL`) and at most one per-book override per
  user.

### 9.2 Migrations

| # | Migration | What it does |
|---|---|---|
| 1 | `InitialCreate` | All six tables, all FKs, plus a raw-SQL `tsvector` generated column + GIN index on `Chapters.ContentText` (full-text search foundation for FR-12/13). |
| 2 | `MakeChapterIdNullable` | `Annotations.ChapterId` and `Bookmarks.ChapterId` → nullable + `SetNull`. |
| 3 | `RestrictUserCascade` | All User-owned FKs → `Restrict` to preserve soft-delete optionality. |
| 4 | `AddBookPublishedYear` | Adds `Books.PublishedYear int?`. |
| 5 | `AddUserAuthFields` | `Username` → `varchar(32)`; adds `PasswordHash`, `LastLoginAt`, `IsActive`; deactivates legacy rows with empty `PasswordHash` (so they fail closed at `IsActive` rather than masquerading as a valid login); creates the functional `LOWER(Username)` unique index. |

The full-text search column (`SearchVector`) is a Postgres **stored
generated column** — `to_tsvector('english', coalesce(ContentText, ''))` —
with a GIN index. EF has no first-class support for generated tsvector
columns, so the column + index are managed via raw SQL inside the
migration. This is the foundation for Phase 1.2 full-text search; no
service code consumes it yet.

### 9.3 [DesignTimeDbContextFactory](../backend/EReader.Data/DesignTimeDbContextFactory.cs)

Used **only** by `dotnet ef` tooling (e.g. `dotnet ef migrations add`).
It bypasses `Program.cs` entirely so generating a migration doesn't
require JWT/Redis config. Connection-string resolution order:
`DATABASE_URL` env → `DATABASE_URL` from a `.env` walked up from CWD →
local default. The `.env` reader is intentionally hand-rolled to avoid
dragging `DotNetEnv` into `EReader.Data` for design-time-only use.

### 9.4 Repositories

[UserRepository](../backend/EReader.Data/Repositories/UserRepository.cs)
and [BookRepository](../backend/EReader.Data/Repositories/BookRepository.cs)
are thin EF wrappers. Two patterns worth flagging:

- **`UserRepository.UpdateAsync` deliberately skips `_db.Update(user)`.**
  The caller fetched the entity through the same scoped `DbContext`, so it's
  already tracked. EF's change-tracker will emit an UPDATE for only the
  columns that actually changed (e.g. just `LastLoginAt` after login).
  Calling `Update()` would force a full-column UPDATE on every save —
  pointless extra writes.

- **`BookRepository.AddAsync` doesn't open an explicit transaction.**
  `SaveChangesAsync` on a single call already opens an implicit transaction
  spanning all queued changes (book + N chapters), so `AddAsync(book)` +
  `AddRangeAsync(chapters)` + `SaveChangesAsync()` is already atomic.

---

## 10. Storage layer — files on disk

`LocalBookFileStore` writes per-book directories under the configured
`BookFilesRoot` (default `../data/books`):

```
backend/data/books/
└── {bookId}/
    ├── source.epub
    └── cover.jpg   (extension preserved from EPUB)
```

- The default path is `../data/books` *relative to the API project's
  working directory*, resolved to absolute at startup. The intent is to
  push uploads **outside** the API project tree so they can't be
  accidentally `git add`-ed. Both `backend/data/` and
  `backend/EReader.Api/data/` are gitignored as a safety net.
- `OpenRead` returns a fresh `FileStream` with `FileShare.Read`, so
  concurrent reads of the same source/cover are fine.
- `DeleteForBook` recursively deletes the per-book directory. Called both
  on book deletion and on rollback during failed ingestion.

`IBookFileStore` is the abstraction boundary — the only seam where a
future S3/Azure Blob backend would plug in.

---

## 11. Tests

`EReader.Tests` runs xUnit + FluentAssertions + Moq + EF Core InMemory.
Coverage is currently focused on the service layer (where the business
logic actually lives — the controllers are dumb mappers, the repositories
are dumb EF wrappers).

| Test file | What it covers |
|---|---|
| [AssetUrlRewriterTests](../backend/EReader.Tests/Services/AssetUrlRewriterTests.cs) | Relative-path resolution, fragment preservation, schemes/data URIs/absolute URLs left alone. |
| [AuthServiceTests](../backend/EReader.Tests/Services/AuthServiceTests.cs) | Register/Login/Refresh/Logout flows, reuse detection, inactive-user handling, constant-message failure. |
| [BookIngestionServiceTests](../backend/EReader.Tests/Services/BookIngestionServiceTests.cs) | Hashing, duplicate detection, orphan cleanup on parse/save failure, file-type validation. |
| [BookServiceTests](../backend/EReader.Tests/Services/BookServiceTests.cs) | Cursor encode/decode, prev/next resolution, 404-on-foreign-user, asset/cover streaming. |
| [UserServiceTests](../backend/EReader.Tests/Services/UserServiceTests.cs) | Username change conflicts, password change with/without revoke-other-sessions. |

Test names follow the project convention:
`Should_[ExpectedResult]_When_[Condition]`. Tests use `Moq` for
service-layer dependencies and `Microsoft.EntityFrameworkCore.InMemory`
for any repository-level checks that need a `DbContext`.

`EReader.Core` declares `[assembly: InternalsVisibleTo("EReader.Tests")]`
in its `.csproj`, which is how the test project reaches internal helpers
like `BookService.EncodeCursor` and `CredentialValidator`.

---

## 12. Frontend deep-dive (for engineers new to React Native)

This section assumes you know React but **not** React Native / Expo. It teaches
the platform concepts as it walks the code, so you can be productive without a
separate RN tutorial. If you already know RN, skim §12.0 and start at §12.1.

The app is **Expo SDK 56 · React 19 · React Native 0.85 · React Native Web**,
primarily targeting the **web** build today (the native paths are wired but a
couple of features stub out on native — flagged below).

### 12.0 The 5-minute React Native mental model

If you've only done web React, here's what's different:

- **Primitives, not DOM.** There is no `<div>`/`<span>`/`<img>`. You compose
  `<View>` (a box, ≈ `div`), `<Text>` (all text must live inside one),
  `<Pressable>` (tap target), `<TextInput>`, `<FlatList>` (virtualised list),
  `<Modal>`, `<Image>`. These come from `react-native`.
- **Styling is JavaScript objects**, not CSS files. You write
  `StyleSheet.create({ row: { flexDirection: 'row', gap: 8 } })` and pass
  `style={styles.row}`. Layout is **flexbox by default**, and the default
  `flexDirection` is `column` (not `row` like the web). There's no cascade and
  no class names — styles are explicit per element, and you compose them with
  arrays: `style={[styles.row, { color: theme.colors.text }]}`.
- **Expo** is a batteries-included toolchain on top of RN: a bundler (**Metro**),
  a dev client, and vetted native modules (`expo-secure-store`,
  `expo-document-picker`, …). You rarely touch Xcode/Gradle.
- **React Native Web** compiles those same primitives to real DOM elements, so
  one component tree runs on web *and* native. That's why this codebase looks
  like RN even though we mostly run it in a browser.
- **`expo-router`** gives **file-based routing** (think Next.js): a file under
  `app/` *is* a route. It sits on top of React Navigation.
- **Metro platform extensions** are the key trick to know: when you import
  `./tokenStorage`, Metro resolves `tokenStorage.web.ts` on web and
  `tokenStorage.native.ts` on native, falling back to `tokenStorage.ts`. We use
  this to keep platform-specific code (secure storage, the reader surface)
  behind a single import with one shared type contract.

### 12.1 Routing & the provider stack

Routes live in [`frontend/app/`](../frontend/app/). The files there are **thin
re-export wrappers** — the real screens live in `src/screens/` so they stay
unit-testable without the router:

```tsx
// app/(authed)/library.tsx
import LibraryScreen from '../../src/screens/LibraryScreen';
export default LibraryScreen;
```

The route map:

| File | URL | Notes |
|---|---|---|
| `app/index.tsx` | `/` | Redirects to `/library` or `/login` by auth state. |
| `app/login.tsx`, `app/register.tsx` | `/login`, `/register` | Public. |
| `app/(authed)/_layout.tsx` | — | **Auth gate** for the group. |
| `app/(authed)/library.tsx` | `/library` | Library grid. |
| `app/(authed)/search.tsx` | `/search` | Full-text search. |
| `app/(authed)/reader/[bookId].tsx` | `/reader/:bookId` | Dynamic segment. |

Two `expo-router` concepts in play:
- **Route groups** — a folder in parentheses like `(authed)` organises routes
  *without* adding a URL segment. So `/library`, not `/authed/library`. Its
  `_layout.tsx` wraps every child route; here it's the auth gate that
  `<Redirect href="/login" />`s anyone unauthenticated
  ([app/(authed)/_layout.tsx](../frontend/app/(authed)/_layout.tsx)).
- **Dynamic segments** — `[bookId].tsx` captures a path param, read in the screen
  via `useLocalSearchParams<{ bookId: string }>()`.

The root layout [app/_layout.tsx](../frontend/app/_layout.tsx) mounts the global
provider stack. **Order matters:**

```
SafeAreaProvider          (insets for notches/status bars)
  └─ QueryProvider        (React Query client — server state)
       └─ AuthProvider    (session state; calls useQueryClient, so must be inside)
            └─ ThemeProvider (reads reading-settings via a hook, needs Auth + Query)
                 └─ <Slot />  (the matched route renders here)
```

`<Slot />` is expo-router's "render the current child route" placeholder (like
`{children}` for the router).

### 12.2 State architecture — Context vs React Query (the load-bearing rule)

Per [best-practices.md](../best-practices.md), there's a hard split:

- **Server state** (anything that lives in the DB: books, chapters, settings,
  search results) → **React Query**, always through a hook in `src/hooks/`.
  Never fetched in a component body.
- **Client/session state** (am I logged in? what theme?) → **React Context**,
  in `src/providers/`.

This is why you'll see *two* providers that look similar but aren't:
`AuthProvider` (pure client state) and `ThemeProvider` (which *reads* server
state via a hook but exposes it as context for convenience). Server data never
gets copied into Context as a source of truth — Context just reflects what the
query cache holds.

[QueryProvider](../frontend/src/providers/QueryProvider.tsx) configures the
client once (held in `useState` so it survives re-renders): `staleTime: 30s`,
`retry: 1`, `refetchOnWindowFocus: false`, mutations don't retry.

### 12.3 Auth & the token lifecycle

This is the most intricate non-reader part of the frontend. Four pieces:

**1. Platform-split token storage.** [tokenStorage.ts](../frontend/src/services/tokenStorage.ts)
defines the `TokenStorage` interface + `StoredTokens` type and a no-op fallback.
Metro swaps in the real implementation per platform:
- [tokenStorage.web.ts](../frontend/src/services/tokenStorage.web.ts) — `localStorage`.
- [tokenStorage.native.ts](../frontend/src/services/tokenStorage.native.ts) — `expo-secure-store` (encrypted keychain/keystore).

Both wipe the entry on a JSON parse failure so a corrupted blob can't wedge the
app into a permanent failure loop.

**2. The axios instance + interceptors** ([services/api.ts](../frontend/src/services/api.ts)):
- A **request interceptor** reads the stored access token and sets
  `Authorization: Bearer …` on every outgoing request.
- A **response interceptor** implements transparent refresh. On a `401` that
  isn't itself the refresh call and hasn't already been retried, it runs a
  **single-flight refresh**: concurrent 401s all `await` the *same* refresh
  promise. This matters because refresh tokens rotate on every use (§4.2) — two
  parallel refreshes would invalidate each other and log the user out. On
  success it replays the original request with the new token; on failure it
  clears storage and fires the `onUnauthorized` listeners.

**3. The event bridge.** `api.ts` can't navigate (it has no router). So it
exposes `onUnauthorized(listener)` and [AuthProvider](../frontend/src/providers/AuthProvider.tsx)
subscribes — when refresh gives up, the provider drops to `unauthed`, clears the
user, and calls `queryClient.clear()` to purge cached server data. This keeps the
service layer router-free and the provider network-free.

**4. AuthProvider bootstrap.** On cold start it reads storage and, if a refresh
token exists, **optimistically** sets `authed` *without* a network round trip —
the first real API call will refresh if the access token is stale. The `user`
object is `null` until login/register populates it (a `/users/me` fetch on boot
is noted as future work). `login`/`register`/`logout` live here and update both
context state and (via `services/auth.ts`) the token store.

### 12.4 Data-fetching hooks (React Query patterns)

Every server interaction is a hook in `src/hooks/` wrapping a thin endpoint
wrapper in `src/services/`. Patterns worth internalising:

- **Stable, structured query keys** so invalidation is surgical.
  [useBooks](../frontend/src/hooks/useBooks.ts) keys as
  `['books', 'list', params]`; `useUploadBook` invalidates `['books']`
  (the *prefix*), so every sort/filter variant refetches after an upload.
- **`useQuery` vs `useInfiniteQuery`.** Single resources (a book, a chapter,
  settings) use `useQuery`. Paginated lists (library, search) use
  `useInfiniteQuery` with `getNextPageParam: (last) => last.nextCursor`, and the
  screen flattens `data.pages.flatMap(p => p.items)`. This is the client side of
  the backend's cursor pagination (§6.1).
- **`staleTime` tuned per resource.** Chapter content is immutable once ingested,
  so [useChapter](../frontend/src/hooks/useChapter.ts) uses `staleTime: Infinity`
  (never refetch). Settings use `60s` to avoid refetch storms when the reader and
  settings drawer mount together.
- **`enabled` to gate queries.** `useChapter` is disabled until both ids exist;
  `useSearch` until the query is ≥ 2 chars; `useGlobalSettings(enabled)` only
  runs once `authed` (so it doesn't kick the refresh interceptor mid-login).
- **Optimistic mutations with rollback.** [useUpsertGlobalSettings](../frontend/src/hooks/useReadingSettings.ts)
  cancels in-flight queries, snapshots the cache, writes the new value
  immediately (so the WebView re-skins instantly), and restores the snapshot in
  `onError`. `onSettled` reconciles with the server response. Position updates
  skip rollback on purpose — they're high-frequency and a one-save drift on a
  blip isn't worth the complexity.

### 12.5 The reader & the WebView bridge (the trickiest part)

EPUB chapters are HTML. Rendering arbitrary book HTML with RN primitives is
impractical, so we render it in a **web view surface** and talk to it over a
message bridge. This is platform-split:

- Native → [ReaderWebView.tsx](../frontend/src/components/ReaderWebView.tsx)
  (`react-native-webview`).
- Web → [ReaderWebView.web.tsx](../frontend/src/components/ReaderWebView.web.tsx)
  (an `<iframe srcDoc=…>`).

Both share one type contract and one document builder:

**Building the document** ([lib/webviewScripts.ts](../frontend/src/lib/webviewScripts.ts)).
`buildChapterDocument` returns a full HTML string: the chapter's body HTML
(assets already rewritten to absolute API URLs by the backend, §5.6), plus a
`<style>` block appended *after* the book's own CSS so our typography wins at the
body level while the author's heading/bold/italic styles survive. Typography is
injected as **CSS custom properties** (`--er-font-size`, `--er-bg`, …) sourced
from the user's `ReadingSetting` and the theme's `webview` color subset — so a
settings change re-skins the page by swapping variable values, no re-fetch.

**The bridge.** An inline `<script>` posts messages to the host:
`window.ReactNativeWebView.postMessage(...)` on native, `window.parent.postMessage(...)`
on web. The RN side parses them in `onMessage` (native) or a
`window.addEventListener('message', …)` handler scoped to the iframe (web). Two
message types today: `scroll` (throttled position reports — every 400ms while
scrolling, plus a trailing fire 800ms after the last scroll so the resting
position is always captured) and `ready` (fired after `load`, used to restore
scroll position only once images have laid out).

**Imperative control.** The parent gets a `scrollTo(y)` method via
`useImperativeHandle` + `forwardRef`. On native it `injectJavaScript`s a
`window.scrollTo`; on web it calls `iframe.contentWindow.scrollTo`. `forwardRef`
is RN's standard way to expose an imperative handle from a child component.

**Orchestration** ([ReaderScreen.tsx](../frontend/src/screens/ReaderScreen.tsx)):
- Seeds the current chapter from (in priority order) a search-result `anchor`
  param → the saved `lastChapterId` → the first chapter.
- Drives chapter navigation (prev/next buttons, the TOC drawer) by swapping
  local `currentChapterId` state, which re-runs `useChapter`.
- **Debounced position persistence**: scroll messages reset a ~1.2s timer; on
  fire (and on chapter change / unmount) it `mutate`s the `/position` endpoint.
  This is two layers of throttling — the in-page script throttles raw scroll, the
  screen debounces the API write on top.

### 12.6 Theming

[theme/tokens.ts](../frontend/src/theme/tokens.ts) holds two flat palettes
(`lightColors`, `darkColors`). Each carries a nested `webview` subset — the only
colors injected into chapter HTML — so the reader surface re-skins independently
of the app chrome.

[ThemeProvider](../frontend/src/providers/ThemeProvider.tsx) reads the user's
global `ReadingSetting`, collapses `"system"` to a concrete palette using
`Appearance.getColorScheme()` (and subscribes to OS changes), and exposes two
hooks: `useTheme()` (colors + resolved mode — what most chrome needs) and
`useThemeContext()` (full typography settings + an optimistic `updateGlobal`).
Until the settings query resolves it serves `DEFAULT_SETTING`, whose values match
the backend `ReadingSetting` model defaults so there's no flash of wrong styling.

### 12.7 Screen tour

- **[LibraryScreen](../frontend/src/screens/LibraryScreen.tsx)** — a responsive
  `FlatList` cover grid. Column count is computed from `useWindowDimensions()`;
  note the `key={cols-N}` trick that forces a re-mount when `numColumns` changes,
  because `FlatList` can't change that prop in place. Infinite scroll via
  `onEndReached` → `fetchNextPage`. A floating "+" button opens
  `expo-document-picker` (filtered to `application/epub+zip`) and uploads via the
  `useUploadBook` mutation. Upload input is itself platform-split: a web `File`
  vs RN's `{ uri, name, type }` FormData shape ([services/books.ts](../frontend/src/services/books.ts)).
- **[SearchScreen](../frontend/src/screens/SearchScreen.tsx)** — a 300ms debounced
  text input feeds `useSearch` (disabled under 2 chars). Tapping a hit navigates
  to the reader with `params: { bookId, anchor: chapterId }`. The server snippet
  is HTML with `<mark>` tags, but there's no native HTML renderer wired up for
  v1, so it's run through `stripHtml` before display — a deliberate v1 shortcut.
- **[ReaderScreen](../frontend/src/screens/ReaderScreen.tsx)** — §12.5.
- **[SettingsDrawer](../frontend/src/components/SettingsDrawer.tsx)** &
  **[TableOfContents](../frontend/src/components/TableOfContents.tsx)** — both use
  RN `<Modal>` with the standard backdrop pattern: a full-screen `Pressable`
  closes on tap, and an inner `Pressable` with an empty `onPress` stops the tap
  from propagating through the panel. The settings drawer edits *global* settings
  (per-book override is intentionally out of v1 scope) and fires updates
  fire-and-forget, leaning on the hook's optimistic cache write for snappiness.

### 12.8 Authenticated images — the `<img>` header problem

Cover and asset endpoints require a Bearer token, but a plain `<Image src=…>` /
`<img>` can't attach an `Authorization` header.
[AuthImage](../frontend/src/components/AuthImage.tsx) solves this on web by
fetching the bytes with axios (so the interceptor adds the header), converting
the blob to an object URL, and feeding that to `<Image>` — revoking the object
URL on unmount to avoid a leak. On native it currently renders the placeholder
(native `<Image>` supports request headers directly; wiring that is deferred
until native is on the roadmap). This is a known native gap, not a bug.

### 12.9 Error handling

[services/errors.ts](../frontend/src/services/errors.ts) `extractApiError` digs
the backend's `{ error: { code, message } }` envelope (§8) out of an axios error,
falling back to a generic message. Screens render `extractApiError(query.error).message`
in their error states, so the UI surfaces the server's own message verbatim.

### 12.10 Gotchas for newcomers

- **All text needs a `<Text>`.** A bare string in a `<View>` throws on native.
- **`flexDirection` defaults to `column`.** Reach for `'row'` explicitly.
- **`gap` works in RN** (recent versions) — used throughout instead of margin
  hacks.
- **Don't fetch in components.** It will fail review. Add a hook in `src/hooks/`.
- **Mind the `.web`/`.native` split.** If you change `ReaderWebView` or
  `tokenStorage`, change *both* variants and keep the shared type contract in the
  base file in sync.
- **`process.env.EXPO_PUBLIC_*`** is the only env convention that reaches the
  client bundle — `EXPO_PUBLIC_API_URL` points the app at the backend.

### 12.11 Testing & tooling recap

- **Jest + React Native Testing Library** for unit/component tests (`npm test`).
  Query by role/text/label, never test IDs; test hooks with `renderHook`.
- **Playwright** for web E2E (`npm run test:e2e:web`) — auto-starts the Expo web
  server.
- **Detox** for native E2E is configured but **not active** (needs
  `npx expo prebuild` to generate `ios/`/`android/` first).

Conventions enforced by [best-practices.md](../best-practices.md): no `any` (use
`unknown`); one function component per file with co-located single-use types;
data fetching in hooks; Context only for cross-cutting concerns, never server
state.

---

## 13. Dev environment

```bash
# Database + Redis
docker compose -f docker/docker-compose.yml up -d

# Backend
cd backend/EReader.Api
dotnet run
# → http://localhost:5043 (see launchSettings)
# → Swagger at /swagger

# Frontend
cd frontend
npx expo start --web
```

Before first run, copy `.env.example` → `backend/EReader.Api/.env` and
fill in:
- `DATABASE_URL` — Postgres connection string (matches docker-compose
  credentials).
- `JWT__KEY` — at least 32 bytes UTF-8 (`openssl rand -base64 48`).
- `REDIS__CONNECTIONSTRING` — `localhost:6379` for the dockerised Redis.

The double underscore (`__`) in env-var names is the ASP.NET Core
convention for nested config keys (`Jwt:Key` ⇔ `JWT__KEY`).

In Development the API auto-applies pending EF migrations on boot, so you
can pull a branch with new migrations and the first request still works.

---

## 14. House rules (from the project's `CLAUDE.md` / `best-practices.md`)

These aren't code, but they're load-bearing for anyone making changes:

- All API routes are versioned under `/api/v1/...`.
- All errors use the `{ error: { code, message, details } }` shape.
- Controllers stay thin — no business logic, no direct DB access. They
  validate input, call the service, and map to HTTP.
- Every async controller action takes a `CancellationToken` and passes it
  down through every service and repository call.
- All async is truly async (no `.Result` / `.Wait()`); `async void` is
  banned outside event handlers.
- Dependencies are registered by interface, never concrete type.
- Catching `Exception` broadly to swallow errors is forbidden — the global
  middleware handles unhandled exceptions.
- No `any` in TypeScript.
- Data fetching logic lives in hooks, not components.
- Tests are named `Should_[Result]_When_[Condition]` and are fully
  self-contained.

The functional-index rule is the single sharp edge: **if a future
migration touches `Users.Username` (or any column whose unique index name
ends `_Lower`), EF will silently downgrade the functional index to a
non-functional one.** Hand-edit the generated migration to keep the
`CREATE UNIQUE INDEX ... LOWER(...)` form.

---

## 15. Quick reference — request lifecycle (chapter read)

End-to-end, what happens on `GET /api/v1/books/{bookId}/chapters/{chapterId}`:

1. **JWT middleware** validates the bearer token, populates `HttpContext.User`.
2. **Authorize filter** (registered as a fallback policy) lets the request
   through because the user is authenticated.
3. **`BooksController.GetChapter`** runs.
4. `_currentUser.GetCurrentUserId()` pulls `sub` from claims.
5. `_books.GetChapterAsync(bookId, chapterId, userId, assetBaseUrl, ct)`:
   - `BookRepository.GetChapterAsync` — `SELECT … FROM Chapters c WHERE
     c.Id = @cid AND c.BookId = @bid AND c.Book.UserId = @uid` (a single
     query that joins on the navigation property; ownership enforced at
     the DB).
   - If null → `NotFoundException` → middleware → 404 JSON.
   - `BookRepository.GetChapterIdsInSpineOrderAsync` — projection-only
     query, no full rows.
   - `AssetUrlRewriter.Rewrite(chapter.ContentText, chapter.ContentHref,
     "/api/v1/books/{bookId}/assets")`.
6. **`ChapterDetailResponse.From`** maps to the wire DTO.
7. JSON serializer → HTTP 200.

That's the whole stack: middleware → controller → service → repository →
EF/Postgres, plus a pure-domain helper for the URL rewrite. Nothing
loops back, nothing reaches across layers — which is exactly the property
that makes the service layer testable in isolation with Moq.

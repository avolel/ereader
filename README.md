# EReader

A personal ebook-library application. Users register an account, upload their own
`.epub` files, and read them in the browser (or on a mobile device) chapter by
chapter — with full-text search across their library, persisted reading position,
and per-book typography/theme overrides.

This README is the **front door** to the codebase: it explains the architecture,
how to run everything locally, the full API surface, the data model, and the
conventions you must follow when changing code. It is written so a mid-to-senior
engineer can get productive and maintain the project without a hand-off.

> For a deeper, narrative walk-through of the **backend internals** (the auth
> token model, the ingestion pipeline, the Lua refresh script, EF mapping
> decisions) and a **full frontend deep-dive for engineers new to React Native**,
> read [docs/CODE_OVERVIEW.md](docs/CODE_OVERVIEW.md). Two feature areas have their
> own tutorial-style deep-dives: [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md)
> (the WCAG 2.1 AA a11y toolkit) and [docs/LOOKUP.md](docs/LOOKUP.md) (the
> dictionary + Wikipedia reference-lookup feature). This README covers the whole
> system end-to-end and stays current with the frontend.
>
> **Read [best-practices.md](best-practices.md) before writing any code.** It is
> the enforced coding standard and CI/review will hold you to it.

---

## Table of contents

1. [Architecture at a glance](#1-architecture-at-a-glance)
2. [Tech stack](#2-tech-stack)
3. [Repository layout](#3-repository-layout)
4. [Local development setup](#4-local-development-setup)
5. [Backend deep-dive](#5-backend-deep-dive)
6. [Data model & persistence](#6-data-model--persistence)
7. [The HTTP API surface](#7-the-http-api-surface)
8. [Frontend deep-dive](#8-frontend-deep-dive)
9. [Testing](#9-testing)
10. [Conventions & house rules](#10-conventions--house-rules)
11. [Common tasks (runbook)](#11-common-tasks-runbook)
12. [Known sharp edges](#12-known-sharp-edges)

---

## 1. Architecture at a glance

EReader is a three-tier system. The frontend talks to the backend over HTTPS/JSON;
the backend persists relational data in PostgreSQL, refresh tokens in Redis, and
the raw uploaded EPUB bytes + extracted covers in MinIO (S3-compatible object
storage).

```
┌──────────────────────┐   HTTPS / JSON    ┌────────────────────────────┐
│  frontend (Expo RN)  │ ───────────────▶ │  backend (ASP.NET Core 10) │
│  React Native + Web  │                   │  EReader.Api               │
│  expo-router         │ ◀─────────────── │  EReader.Core              │
└──────────────────────┘                   │  EReader.Data              │
                                           └────────────┬───────────────┘
                                          ┌─────────────┼─────────────┐
                                          ▼             ▼             ▼
                                  ┌──────────────┐ ┌─────────┐ ┌────────────────┐
                                  │ PostgreSQL   │ │  Redis  │ │ MinIO (S3 API) │
                                  │ (EF Core 10) │ │ refresh │ │ object storage │
                                  │ + tsvector   │ │ tokens  │ │  {bookId}/...  │
                                  │   full-text  │ └─────────┘ └────────────────┘
                                  └──────────────┘
```

### The backend is three projects, with a strict dependency rule

| Project | Role | May reference |
|---|---|---|
| **`EReader.Api`** | HTTP boundary: controllers, DTOs, middleware, `Program.cs` composition root, ASP.NET auth integration. | `Core` + `Data` |
| **`EReader.Core`** | Pure domain: models, **service interfaces**, business logic in `Services/`, domain exceptions. **No I/O, no HTTP, no EF.** | BCL only |
| **`EReader.Data`** | Adapters that implement `Core`'s interfaces: EF `DbContext`, repositories, EPUB parser, BCrypt hasher, JWT issuer, Redis store, MinIO object store. | `Core` |

The rule — `Core` defines *interfaces*, `Data` provides *implementations*, `Api`
wires them together via DI — is what lets the entire service layer be unit-tested
**without Postgres or Redis running**. Every external dependency sits behind an
interface that tests can fake.

### The frontend is an Expo (React Native + Web) app

File-based routing via `expo-router`, server state via TanStack React Query, auth
and theme via React Context, and the EPUB chapter HTML rendered inside a WebView
(with a `.web.tsx` variant that uses an `<iframe>` on web). It targets web today;
the iOS/Android paths are wired but not the primary focus.

---

## 2. Tech stack

| Layer | Technology | Notes |
|---|---|---|
| Backend API | .NET 10 / ASP.NET Core Web API | Controllers + minimal hosting |
| ORM | EF Core 10 (`Npgsql` provider) | Code-first migrations |
| Database | PostgreSQL 16 | Includes a `tsvector` generated column + GIN index for full-text search |
| Token store | Redis 7 | Opaque refresh tokens, hashed; family-based rotation |
| Object storage | MinIO (S3-compatible) | Source EPUBs + extracted covers, keyed `{bookId}/...`; via `Minio` .NET client |
| Password hashing | BCrypt.Net | Work factor 12 |
| EPUB parsing | VersOne.Epub | Wrapped behind `IEpubParser` |
| Frontend | Expo SDK 56, React 19, React Native 0.85, React Native Web | `expo-router` for routing |
| Server state | TanStack React Query 5 | All data fetching lives in hooks |
| HTTP client | axios | Single instance with auth + refresh interceptors |
| Token storage | `expo-secure-store` (native) / `localStorage` (web) | Platform-split file |
| Language | C# (nullable enabled) / TypeScript (strict, no `any`) | |

---

## 3. Repository layout

```
ereader/
├── backend/
│   ├── EReader.slnx                  ← solution (new XML format)
│   ├── EReader.Api/                  ← HTTP layer
│   │   ├── Auth/                     ← JwtBearerSetup, SwaggerAuthSetup,
│   │   │                               HttpContextCurrentUserService
│   │   ├── Controllers/              ← Auth, Users, Books, Search, Lookup, ReadingSettings
│   │   ├── Dtos/                     ← request + response shapes (one per file)
│   │   ├── Middleware/               ← ErrorHandlingMiddleware (exception → JSON)
│   │   ├── data/dictionary/          ← wordnet.json.gz (offline dictionary dataset)
│   │   ├── Program.cs                ← composition root (DI + pipeline)
│   │   ├── appsettings*.json         ← committed config (URLs, JWT lifetimes, …)
│   │   └── appsettings.*.example.json
│   ├── EReader.Core/                 ← pure domain (no I/O)
│   │   ├── Auth/                     ← AuthTokens, Issued/Consumed refresh-token records
│   │   ├── Books/                    ← ParsedEpub, BookAsset, ChapterContent,
│   │   │                               BookListPage/Sort, SearchHit, BookWithChapters
│   │   ├── Lookups/                  ← DictionaryResult, WikipediaResult
│   │   ├── Exceptions/               ← domain exceptions mapped to HTTP by middleware
│   │   ├── Interfaces/               ← ALL service/repository/adapter contracts
│   │   ├── Models/                   ← User, Book, Chapter, Annotation, Bookmark,
│   │   │                               ReadingSetting, AnnotationType
│   │   ├── ReadingSettings/          ← TypographyUpdate / PositionUpdate inputs
│   │   └── Services/                 ← AuthService, UserService, BookService,
│   │                                   BookIngestionService, SearchService,
│   │                                   ReadingSettingsService, CredentialValidator,
│   │                                   AssetUrlRewriter, DictionaryService, WikipediaService
│   ├── EReader.Data/                 ← adapters
│   │   ├── Auth/                     ← BCryptPasswordHasher, JwtTokenIssuer,
│   │   │                               RedisRefreshTokenStore, JwtOptions, RedisOptions
│   │   ├── Migrations/               ← EF Core migrations + model snapshot
│   │   ├── Parsing/                  ← EpubParserAdapter, ZipEpubAssetReader
│   │   ├── Repositories/             ← User, Book, Search, ReadingSettings
│   │   ├── Storage/                  ← MinioBookFileStore, MinioOptions
│   │   ├── EReaderDbContext.cs
│   │   └── DesignTimeDbContextFactory.cs   ← used only by `dotnet ef` tooling
│   └── EReader.Tests/               ← xUnit + FluentAssertions + Moq + EF InMemory
├── frontend/
│   ├── app/                          ← expo-router file routes
│   │   ├── _layout.tsx               ← provider stack (Query → Auth → Theme)
│   │   ├── index.tsx                 ← redirects by auth state
│   │   ├── login.tsx, register.tsx   ← public routes
│   │   └── (authed)/                 ← auth-gated route group
│   │       ├── _layout.tsx           ← redirects to /login if unauthed
│   │       ├── library.tsx
│   │       ├── search.tsx
│   │       └── reader/[bookId].tsx
│   └── src/
│       ├── components/               ← AuthImage, TableOfContents, SettingsDrawer,
│       │   │                           ConfirmDialog, ReaderWebView(.web).tsx,
│       │   │                           SelectionMenu, LookupOverlay
│       │   └── a11y/                 ← AccessibleModal, IconButton, useFocusTrap,
│       │                               useAnnouncer, SkipToContent, useEscToClose, focusStyles
│       ├── hooks/                    ← useBooks, useBook, useChapter, useSearch, useLookup,
│       │                               useUploadBook, useDeleteBook, useReadingSettings
│       ├── providers/                ← QueryProvider, AuthProvider, ThemeProvider
│       ├── screens/                  ← Library, Reader, Search, Login, Register
│       ├── services/                 ← api (axios), auth, books, search, lookup,
│       │                               readingSettings, tokenStorage(.web/.native), errors
│       ├── theme/tokens.ts           ← light/dark color palettes
│       ├── lib/webviewScripts.ts     ← injected JS for the reader WebView
│       └── types/index.ts            ← shared types mirroring backend DTOs
├── docker/
│   ├── docker-compose.yml            ← PostgreSQL 16 + Redis 7 + MinIO
│   ├── .env.example                  ← compose vars: DB_PASSWORD + MINIO_*
│   └── postgres/init.sql             ← ensures DB exists; EF owns the schema
├── docs/
│   ├── CODE_OVERVIEW.md              ← deep backend walk-through + frontend deep-dive
│   ├── ACCESSIBILITY.md              ← a11y tutorial (focus traps, live regions, primitives)
│   ├── LOOKUP.md                     ← dictionary + Wikipedia lookup tutorial (backend→UI)
│   └── DEV_ENV_SPEC.md               ← original Phase-0 environment spec
├── plans/                            ← phase planning docs
├── scripts/
│   ├── download-test-books.sh        ← fetch sample EPUBs
│   └── build-dictionary.sh           ← build the offline WordNet dataset (wordnet.json.gz)
├── test-books/                       ← sample EPUBs (git-ignored content)
├── best-practices.md                 ← ENFORCED coding standards — read first
├── CLAUDE.md / .claude/CLAUDE.md     ← agent instructions
└── .env.example                      ← backend env-var template
```

---

## 4. Local development setup

### Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0 |
| Node.js | 22 LTS |
| Docker + Compose v2 | latest |
| PostgreSQL client (`psql`) | 16+ (optional, for poking the DB) |

### Step 1 — start Postgres, Redis + MinIO

The compose file lives under `docker/` and runs three services: Postgres, Redis,
and MinIO. It substitutes `$DB_PASSWORD`, `$MINIO_USER`, and `$MINIO_PASSWORD` at
`up` time. These live in **`docker/.env`** (copied from `docker/.env.example`),
which is the source of truth for the *container stack*:

```bash
cp docker/.env.example docker/.env      # then edit the passwords
# Compose auto-loads docker/.env because it sits next to the project it runs:
docker compose --env-file docker/.env -f docker/docker-compose.yml up -d
```

`docker/.env` carries:

| Var | Used by | Meaning |
|---|---|---|
| `DB_PASSWORD` | Postgres container | Postgres superuser password. Must match the `Password=` in the backend's `DATABASE_URL` (Step 2). |
| `MINIO_USER` / `MINIO_PASSWORD` | MinIO container **and** the API | MinIO root credentials. The API reads the *same* values as its access/secret key. |
| `MINIO_BUCKET` | API | Bucket name (default `ereader-media`); auto-created on boot. |
| `MINIO_ENDPOINT` | API | `host:port`, **no scheme** (default `localhost:9000`). |

> If you bring the Postgres volume up once with the wrong/empty `DB_PASSWORD`,
> Postgres bakes it into the data volume on first init. Changing it later means
> `docker compose ... down -v` to drop the volume and re-init (see §11).

Verify Postgres is reachable:

```bash
psql -h localhost -U ereader -d ereader -c "\conninfo"
```

Services exposed: PostgreSQL on `localhost:5432`, Redis on `localhost:6379`, MinIO
S3 API on `localhost:9000`, MinIO console UI on `http://localhost:9001`.

### Step 2 — configure the backend

Copy the template and fill in real values:

```bash
cp backend/EReader.Api/.env.example backend/EReader.Api/.env
```

The backend `.env` ships these keys:

| Var | Meaning |
|---|---|
| `DATABASE_URL` | Npgsql connection string. Its `Password=` must equal `DB_PASSWORD` in `docker/.env`. e.g. `Host=localhost;Port=5432;Database=ereader;Username=ereader;Password=ereader_dev` |
| `JWT__KEY` | HS256 signing key, **≥ 32 bytes UTF-8**. Generate with `openssl rand -base64 48`. |
| `REDIS__CONNECTIONSTRING` | `localhost:6379` for the dockerised Redis. |

> ⚠️ **MinIO creds gap.** The API also reads `MINIO_ENDPOINT`, `MINIO_USER`,
> `MINIO_PASSWORD`, and `MINIO_BUCKET` from its configuration, and **throws at
> boot if `MINIO_USER`/`MINIO_PASSWORD` are missing** ([Program.cs](backend/EReader.Api/Program.cs)).
> The backend `.env.example` does **not** yet include them — add the same four
> `MINIO_*` lines from `docker/.env` to `backend/EReader.Api/.env` (or export them
> in the API's shell) or `dotnet run` will fail fast.

> The double underscore (`__`) is the ASP.NET Core convention for nested config
> keys: `JWT__KEY` binds to `Jwt:Key`, `REDIS__CONNECTIONSTRING` to
> `Redis:ConnectionString`. The `MINIO_*` keys are read as flat config keys, not
> nested. Non-secret settings (lifetimes, page sizes, URLs) live in committed
> `appsettings*.json`. The `.env` is loaded **only in Development** via
> `DotNetEnv` — in production these are real environment variables and no `.env`
> should exist.

### Step 3 — run the backend

```bash
cd backend/EReader.Api
dotnet run
```

- In Development, the API **auto-applies pending EF migrations on boot**, so a
  freshly pulled branch with new migrations won't 500 the first request.
- Swagger UI is served at `/swagger` (the root `/` redirects there). Swagger is
  pre-wired with a Bearer auth prompt so you can paste a JWT and call protected
  endpoints.
- The listen URL comes from `launchSettings.json` / `ASPNETCORE_URLS`. The
  frontend defaults to `http://localhost:5000`; if the backend binds a different
  port, set `EXPO_PUBLIC_API_URL` for the frontend (see below).

### Step 4 — run the frontend

```bash
cd frontend
npm install
npx expo start --web      # web target → http://localhost:8081
# or: npx expo start       # for Expo Go / simulators
```

Point the app at a non-default API with `EXPO_PUBLIC_API_URL` (copy
`frontend/.env.example` → `frontend/.env` and edit, or set it inline):

```bash
EXPO_PUBLIC_API_URL=http://localhost:5043 npx expo start --web
```

Dev CORS is configured in `Program.cs` to allow `localhost:8081`/`19006` (and the
`127.0.0.1` equivalents) with credentials. Production CORS is intentionally not
configured — set it per-environment at deploy time.

#### Running on Android (native)

The backend already binds `0.0.0.0:5000` (see `appsettings.Development.json`), so
it's reachable from an emulator/device. The only thing that changes is the API URL
the app points at — `localhost` inside the app means the device, not your machine:

| Target | `EXPO_PUBLIC_API_URL` |
|---|---|
| Web / iOS simulator | `http://localhost:5000` |
| Android emulator | `http://10.0.2.2:5000` |
| Physical device (same Wi-Fi) | `http://<your-LAN-IP>:5000` |

Native builds use [EAS Build](https://docs.expo.dev/build/introduction/) (managed
workflow — no `ios/`/`android/` in git). Profiles live in `frontend/eas.json`;
`expo-build-properties` permits cleartext HTTP for the `development`/`preview`
profiles only (production requires an HTTPS backend). First run:

```bash
npm i -g eas-cli
eas login
eas init                                          # writes extra.eas.projectId
eas build --platform android --profile development
# install the dev-client APK on the emulator/device, then:
npx expo start --dev-client
```

> Covers and in-chapter images on native authenticate differently from web:
> the cover `<Image>` sends a Bearer header, and in-chapter WebView assets carry
> the access token as an `?access_token=` query param (honoured only on the media
> GET routes — see `JwtBearerSetup.cs` / `MediaQueryToken.cs`).

### Step 5 — get some books

```bash
./scripts/download-test-books.sh   # pulls sample EPUBs into test-books/
```

Then upload them through the app (Library screen → upload) or via
`POST /api/v1/books` in Swagger.

---

## 5. Backend deep-dive

This is a summary; [docs/CODE_OVERVIEW.md](docs/CODE_OVERVIEW.md) has the
line-by-line treatment of the auth subsystem and ingestion pipeline.

### 5.1 Composition root — [Program.cs](backend/EReader.Api/Program.cs)

The only place concrete types are bound to interfaces. Key decisions:

- **Auth-by-default.** A fallback `AuthorizeFilter` requiring an authenticated
  user is added to every controller. Endpoints opt *out* with `[AllowAnonymous]`
  (only `register`, `login`, `refresh`, and the `/` → `/swagger` redirect do).
- **DI lifetimes are deliberate** and follow best-practices:
  - **Singleton** (stateless / thread-safe shared state): `IPasswordHasher`,
    `IJwtTokenIssuer` (holds prebuilt signing credentials),
    `IRefreshTokenStore` (deps are the singleton Redis multiplexer + options),
    `IBookFileStore` → `MinioBookFileStore` (wraps the thread-safe `IMinioClient`
    + bucket name), `IConnectionMultiplexer`, `IMinioClient`.
  - **Transient** (stateless wrappers): `IEpubParser`, `IEpubAssetReader`.
  - **Scoped** (touch the scoped `DbContext`): every repository, plus
    `IAuthService`, `IUserService`, `IBookService`, `IBookIngestionService`,
    `ISearchService`, `IReadingSettingsService`, `ICurrentUserService`.
- **Fail-fast config.** `RedisOptions` is `ValidateOnStart` — a missing
  connection string crashes at boot, not on the first refresh. The JWT key is
  validated to be ≥ 32 bytes UTF-8.
- **Pipeline order:** `ErrorHandlingMiddleware` is **outermost** (it must catch
  exceptions from auth too) → (dev: CORS, auto-migrate, Swagger) →
  `UseAuthentication` → `UseAuthorization` → `MapControllers`.

### 5.2 Authentication & authorization

Two distinct credentials:

| Credential | Format | Storage | Default lifetime |
|---|---|---|---|
| **Access token** | JWT (HS256), claims `sub`, `preferred_username`, `jti`, `fid` | Stateless, validated cryptographically per request | 15 min |
| **Refresh token** | 32-byte CSPRNG, base64url | SHA-256 hash + metadata in Redis | 30 days |

- **Refresh tokens are opaque** (not JWTs). All state is in Redis under three key
  shapes (`token`, `family`, `user`) giving O(1) revocation at three
  granularities without scanning.
- **Token families & reuse detection.** Every login starts a *family*. Each
  refresh rotates *within* the family (consume token N, issue N+1, same
  `familyId`). Presenting an already-consumed token is treated as theft → the
  **entire family is revoked** and the caller gets `REFRESH_REUSED`. The atomic
  consume is a Lua script (single round trip read-then-write) so concurrent
  refreshes can't both succeed.
- **`fid` claim** stamps the family into the access token. That's what powers
  `revokeOtherSessions: true` on change-password — kill every family *except* the
  current one.
- **Why SHA-256, not BCrypt, on refresh tokens:** they're already 256 bits of
  uniform randomness, so there's no dictionary attack to slow down. BCrypt is
  reserved for passwords, where slowing guesses matters.
- **Constant-message auth failures:** "no such user" and "wrong password" return
  the identical `INVALID_CREDENTIALS` so the API doesn't leak which usernames
  exist. Inactive users fail with the same message.
- **Controllers never touch `HttpContext`.** They depend on
  `ICurrentUserService` ([HttpContextCurrentUserService](backend/EReader.Api/Auth/HttpContextCurrentUserService.cs))
  which reads `sub` (→ `NameIdentifier`) and the `fid` claim, keeping ASP.NET
  types out of the service layer.

### 5.3 Book ingestion pipeline

`POST /api/v1/books` (multipart) →
[BookIngestionService.IngestAsync](backend/EReader.Core/Services/BookIngestionService.cs):

1. Buffer the upload to a `MemoryStream` (the `IFormFile` stream is forward-only;
   we need to read it twice — once to hash, once to persist).
2. SHA-256 the bytes; reject duplicates **per user** (`ConflictException` →
   409 `DUPLICATE_BOOK`). A `(UserId, FileHash)` unique index backstops races.
3. `PUT` the EPUB to MinIO at object key `{bookId}/source.epub` via
   `MinioBookFileStore.SaveSourceAsync`; the returned object key is stored on
   `Book.FilePath`.
4. Parse with [EpubParserAdapter](backend/EReader.Data/Parsing/EpubParserAdapter.cs)
   (VersOne.Epub) → `ParsedEpub` (metadata, cover bytes, chapters in spine order).
   All parser exceptions become `MalformedEpubException` (422); cancellation is
   preserved.
5. Save the cover if present (`{bookId}/cover.{ext}` → `Book.CoverImagePath`).
6. `BookRepository.AddAsync(book, chapters)` — a single `SaveChanges`, atomic.
7. **On any failure above the DB write**, the `catch` calls
   `DeleteForBookAsync(bookId)` — lists and removes every object under the
   `{bookId}/` prefix so no orphaned blobs linger (the DB row was never committed).

### 5.4 Reading a chapter

Chapters store the inner HTML of `<body>` only. At **read time** (not ingest),
[AssetUrlRewriter](backend/EReader.Core/Services/AssetUrlRewriter.cs) rewrites
relative asset refs into absolute API URLs:

```
Stored:  <img src="../Images/fig1.png">         (chapter at OEBPS/Text/ch1.xhtml)
Served:  <img src="/api/v1/books/{id}/assets/OEBPS/Images/fig1.png">
```

Rewriting at read time keeps the DB decoupled from the API's route prefix.
Absolute URLs, fragments (`#…`), protocol-relative (`//…`), `data:` URIs, and
anything with a URL scheme are deliberately left alone. To serve an asset, the
source EPUB is pulled from MinIO into a **seekable `MemoryStream`** (the object is
buffered because `ZipArchive` needs random access — `OpenReadAsync`), then read
on demand from the zip
([ZipEpubAssetReader](backend/EReader.Data/Parsing/ZipEpubAssetReader.cs)) — never
extracted to disk. An `ArchiveOwningStream` wrapper (`leaveOpen: false`) means
disposing the response stream closes the `ZipArchive` *and* the buffered source
stream behind it.

### 5.5 Full-text search

`GET /api/v1/search?q=…` →
[SearchService](backend/EReader.Core/Services/SearchService.cs) (validates 2–256
chars, decodes the cursor) →
[SearchRepository](backend/EReader.Data/Repositories/SearchRepository.cs):

- Raw SQL against the Postgres `tsvector` **generated column** `Chapters.SearchVector`
  (EF has no first-class type for it). `websearch_to_tsquery` parses friendly
  query syntax (quoted phrases, `AND/OR`, `-negation`) and never throws on bad
  input.
- `ts_headline` builds the highlighted `<mark>…</mark>` snippet from the original
  `ContentText`.
- Results are scoped to the user (`b."UserId" = @userId`), optionally filtered by
  `bookId`, keyset-paginated on `(BookId, SpineOrder)`, fetching `pageSize + 1`
  rows to compute `hasMore` without a `COUNT`.

> The snippet is HTML with `<mark>` tags — **it must be sanitized on render.**
> The frontend type comments flag this.

### 5.6 Reading settings (typography, theme, position)

[ReadingSettingsService](backend/EReader.Core/Services/ReadingSettingsService.cs)
manages one **global default** row per user (`BookId NULL`) and optional
**per-book override** rows. `GetForBook` falls back: per-book override → global →
transient defaults. Two separate input types intentionally split the concerns:

- **`TypographyUpdate`** — theme/font/size/spacing/margins, all optional (null =
  leave as-is), validated against allow-lists and numeric ranges. Applies to
  global or per-book.
- **`PositionUpdate`** — `{ chapterId, scrollOffset }`, always per-book, updates
  far more often. Seeds a per-book row carrying just position if none exists.

### 5.7 Error model

Every 4xx/5xx is the same JSON shape, produced by
[ErrorHandlingMiddleware](backend/EReader.Api/Middleware/ErrorHandlingMiddleware.cs):

```json
{ "error": { "code": "RESOURCE_NOT_FOUND", "message": "...", "details": null } }
```

| Exception | HTTP | Code(s) |
|---|---|---|
| `ValidationException` | 400 | `VALIDATION_ERROR` (+ optional `details`) |
| `AuthenticationException` | 401 | `INVALID_CREDENTIALS` / `REFRESH_INVALID` / `REFRESH_REUSED` / `NO_USER` |
| `AuthorizationException` | 403 | `FORBIDDEN` |
| `NotFoundException` | 404 | `RESOURCE_NOT_FOUND` |
| `ConflictException` | 409 | `RESOURCE_CONFLICT` / `USERNAME_TAKEN` / `DUPLICATE_BOOK` |
| `UnsupportedFileException` | 415 | `UNSUPPORTED_MEDIA_TYPE` |
| `MalformedEpubException` | 422 | `EPUB_MALFORMED` |
| anything else | 500 | `INTERNAL_ERROR` (logged at `Error`) |

The domain throws exceptions; controllers stay one-liners on the happy path and
let everything else propagate to the middleware. "Belongs to another user"
returns **404, not 403**, throughout the read paths (don't leak existence).

---

## 6. Data model & persistence

[EReaderDbContext](backend/EReader.Data/EReaderDbContext.cs) — six entities:

```
User ──┬──< Book ──< Chapter
       │       └──< Annotation  (ChapterId nullable, SetNull)
       │       └──< Bookmark    (ChapterId nullable, SetNull)
       ├──< Annotation
       ├──< Bookmark
       └──< ReadingSetting  (UserId, BookId) unique; BookId NULL = global default
```

Key mapping decisions (the non-obvious ones):

- **`Users.Username` has a *functional* unique index on `LOWER(username)`.** EF
  can't model functional indexes, so `OnModelCreating` declares a regular unique
  index named `IX_Users_Username_Lower`, and the migration is **hand-edited** to
  emit `CREATE UNIQUE INDEX … LOWER(...)`. ⚠️ **Any future migration touching
  `Users.Username` must be hand-edited** the same way, or case-insensitive
  uniqueness silently regresses. See best-practices §"Functional Indexes" — this
  is the single sharpest edge in the repo.
- **`Book → User` is `OnDelete(Restrict)`** (and so are Annotation/Bookmark/
  ReadingSetting → User). Deleting a user does *not* cascade-wipe their library;
  the service layer must act explicitly. This keeps a future soft-delete /
  anonymize path open without DB rework. **`Book → Chapter/Annotation` stays
  `Cascade`** so deleting a book cleans up normally.
- **`Annotation.ChapterId` / `Bookmark.ChapterId` are nullable + `SetNull`.** A
  chapter can be regenerated on EPUB re-import without dropping annotations;
  `TextAnchor` is the durable identity, `ChapterId` is re-resolved when null.
- **`Chapter` has a unique `(BookId, SpineOrder)`** — no two chapters share a
  position.
- **`Chapters.SearchVector`** is a Postgres **stored generated column**
  (`to_tsvector('english', coalesce(ContentText,''))`) with a GIN index, managed
  by raw SQL inside the migration.

### Migrations

| # | Migration | What it does |
|---|---|---|
| 1 | `InitialCreate` | Six tables + FKs + the `tsvector` generated column & GIN index. |
| 2 | `MakeChapterIdNullable` | Annotation/Bookmark `ChapterId` → nullable + `SetNull`. |
| 3 | `RestrictUserCascade` | User-owned FKs → `Restrict`. |
| 4 | `AddBookPublishedYear` | `Books.PublishedYear int?` (regex-extracted for sort/filter). |
| 5 | `AddUserAuthFields` | `Username varchar(32)`, `PasswordHash`, `LastLoginAt`, `IsActive`; deactivates legacy rows; creates the functional `LOWER(Username)` index. |
| 6 | `AddReadingPositionToReadingSetting` | `LastChapterId`, `LastScrollOffset`, `LastReadAt` on `ReadingSetting`. |

[DesignTimeDbContextFactory](backend/EReader.Data/DesignTimeDbContextFactory.cs)
exists **only** for `dotnet ef` tooling — it resolves the connection string
(env → `.env` walked up from CWD → local default) without needing JWT/Redis
config, so generating a migration doesn't require a full app boot.

---

## 7. The HTTP API surface

All routes are under `/api/v1`. All require a Bearer access token except the three
marked **public**. All list endpoints are cursor-paginated.

### Auth — [AuthController](backend/EReader.Api/Controllers/AuthController.cs)

| Verb | Route | Notes |
|---|---|---|
| POST | `/auth/register` | **public.** Creates user + first session. `201` with `Location: /api/v1/users/me`. |
| POST | `/auth/login` | **public.** Constant-message failure. |
| POST | `/auth/refresh` | **public.** Rotates within the family; detects reuse. |
| POST | `/auth/logout` | Revokes the supplied refresh token. |
| POST | `/auth/logout-all` | Revokes every family for the current user. |

### Users — [UsersController](backend/EReader.Api/Controllers/UsersController.cs)

| Verb | Route | Notes |
|---|---|---|
| GET | `/users/me` | Current profile (action name `Me` so register's `CreatedAtAction` resolves). |
| PATCH | `/users/me` | Rename. Null `Username` → `204` without hitting the DB. |
| POST | `/users/me/change-password` | Action-style (auth carve-out). `RevokeOtherSessions` flag kills all *other* families. |

### Books — [BooksController](backend/EReader.Api/Controllers/BooksController.cs)

| Verb | Route | Notes |
|---|---|---|
| POST | `/books` | Upload EPUB (multipart, 100 MB cap). |
| GET | `/books` | List library. Query: `cursor`, `pageSize` (default 20, max 100), `sort` (`importedAt`/`title`/`author`), `dir`, `author`, `language`. |
| GET | `/books/{bookId}` | Detail + table of contents. |
| GET | `/books/{bookId}/chapters/{chapterId}` | Chapter HTML (asset URLs rewritten) + prev/next chapter IDs. |
| GET | `/books/{bookId}/assets/{*path}` | Stream an embedded image/css/font from the EPUB zip. |
| GET | `/books/{bookId}/cover` | Stream the extracted cover. |
| DELETE | `/books/{bookId}` | Hard-delete book + cascade chapters + delete files. |

### Search — [SearchController](backend/EReader.Api/Controllers/SearchController.cs)

| Verb | Route | Notes |
|---|---|---|
| GET | `/search` | Query: `q` (2–256 chars), `bookId` (optional filter), `cursor`, `pageSize` (default 20, max 50). Returns hits with `<mark>` snippets. |

### Lookup — [LookupController](backend/EReader.Api/Controllers/LookupController.cs)

Reference lookups for a selected word/term. **Both always return `200` with a
`found` flag in the body — "not found" is data, not a 404.** Full detail in
[docs/LOOKUP.md](docs/LOOKUP.md).

| Verb | Route | Notes |
|---|---|---|
| GET | `/lookup/define` | Query: `word`. Offline WordNet dictionary (in-memory). `{ word, found, senses[] }`. Synchronous (no I/O). |
| GET | `/lookup/wikipedia` | Query: `term`. Proxies Wikipedia's REST summary API via a typed `HttpClient`. `{ term, found, title, extract, pageUrl, thumbnailUrl }`. |

### Reading settings — [ReadingSettingsController](backend/EReader.Api/Controllers/ReadingSettingsController.cs)

| Verb | Route | Notes |
|---|---|---|
| GET | `/reading-settings/me` | Global default (typography + theme). |
| PUT | `/reading-settings/me` | Upsert global typography/theme. |
| GET | `/reading-settings/books/{bookId}` | Per-book settings (falls back to global). |
| PUT | `/reading-settings/books/{bookId}` | Upsert per-book override. |
| DELETE | `/reading-settings/books/{bookId}` | Remove per-book override (idempotent). |
| PUT | `/reading-settings/books/{bookId}/position` | Save reading position `{ chapterId, scrollOffset }`. |

---

## 8. Frontend deep-dive

### Routing & provider stack

`expo-router` does file-based routing. [app/_layout.tsx](frontend/app/_layout.tsx)
mounts the provider stack:

```
SafeAreaProvider
  └─ QueryProvider              (TanStack React Query client)
       └─ AuthProvider          (session state; needs the query client to clear cache)
            └─ ThemeProvider     (reads global reading-settings once authed)
                 └─ AnnouncerProvider  (app-level a11y live region)
                      ├─ SkipToContent (web-only "skip to main content" link)
                      └─ <Slot />       (routes)
```

- [app/index.tsx](frontend/app/index.tsx) redirects to `/library` or `/login` by
  auth status.
- [app/(authed)/_layout.tsx](frontend/app/(authed)/_layout.tsx) is the **auth
  gate** — the `(authed)` parens make it a route *group* (the segment isn't in the
  URL), and it redirects to `/login` when unauthed. Library, search, and
  `reader/[bookId]` live under it.

### Auth & the axios refresh dance

[services/api.ts](frontend/src/services/api.ts) is the single axios instance with
two interceptors:

- **Request:** injects `Authorization: Bearer <accessToken>` from `tokenStorage`.
- **Response:** on a `401` (that isn't itself the refresh call and hasn't already
  retried), it runs a **single-flight refresh** — concurrent 401s all await the
  *same* refresh promise, because family rotation means parallel refreshes would
  invalidate each other. If refresh fails, it clears tokens and notifies
  `onUnauthorized` listeners. [AuthProvider](frontend/src/providers/AuthProvider.tsx)
  subscribes to that and drops to the `unauthed` state (clearing the query cache).

[AuthProvider](frontend/src/providers/AuthProvider.tsx) bootstraps optimistically:
if a refresh token exists in storage it assumes `authed` without a network round
trip — the first real API call refreshes if the access token is stale. Tokens are
stored via a platform-split module: `tokenStorage.web.ts` (`localStorage`) vs
`tokenStorage.native.ts` (`expo-secure-store`).

### The reader

[ReaderScreen](frontend/src/screens/ReaderScreen.tsx) is the most involved screen:

- Loads book detail (`useBook`), settings (`useBookSettings`), and the current
  chapter (`useChapter`). It seeds `currentChapterId` from a `search`-result
  anchor if present, else from the saved `lastChapterId`, else the first chapter.
- Renders the rewritten chapter HTML in
  [ReaderWebView](frontend/src/components/ReaderWebView.tsx) — a `react-native-webview`
  on native, an `<iframe>` on web (`ReaderWebView.web.tsx`). Injected JS
  ([lib/webviewScripts.ts](frontend/src/lib/webviewScripts.ts)) applies typography
  and reports throttled scroll position back via `postMessage`.
- **Position persistence is debounced** (~1.2 s of scroll idle) and also flushed
  on chapter change/unmount, calling `useUpdatePosition` → the `/position`
  endpoint. Prev/next navigation and the `TableOfContents` drawer drive chapter
  changes; the `SettingsDrawer` edits typography/theme.
- **Deleting a book** is wired into both screens via the reusable
  [ConfirmDialog](frontend/src/components/ConfirmDialog.tsx) (a themed `<Modal>`
  with destructive/busy states, same backdrop-tap pattern as `SettingsDrawer`).
  On the library, each `BookCard` carries an overflow (⋯) button layered over the
  cover's top-right corner (and a long-press on the cover); in the reader, a ⋯
  header button. Both open the dialog → `useDeleteBook` → `router.back()` (reader)
  or list refetch (library). Deletion cascades server-side to chapters, bookmarks,
  highlights, and reading progress, which the confirm copy spells out.

### Theme

[ThemeProvider](frontend/src/providers/ThemeProvider.tsx) reads the user's global
reading-settings (only once `authed`, to avoid spurious 401→refresh during login),
resolves `system` against the OS color scheme, and exposes both a lightweight
`useTheme()` (colors only) and a full `useThemeContext()` (typography + an
optimistic `updateGlobal`). Palettes are in [theme/tokens.ts](frontend/src/theme/tokens.ts).

### Data fetching — hooks only

Per best-practices, **no component fetches in its body.** All server state goes
through hooks in `src/hooks/` (`useBooks`, `useBook`, `useChapter`, `useSearch`,
`useUploadBook`, `useDeleteBook`, `useReadingSettings`) wrapping React Query, with
thin endpoint wrappers in `src/services/`. `useDeleteBook` is a `DELETE` mutation
that invalidates the entire `['books']` key family on success (mirroring
`useUploadBook`) so the removed book disappears from whatever sort/filter is
mounted. Shared types in
[types/index.ts](frontend/src/types/index.ts) mirror the backend DTOs (camelCase).
[AuthImage](frontend/src/components/AuthImage.tsx) exists because cover/asset URLs
are auth-protected — a plain `<img src>` can't send the Bearer header, so it fetches
the bytes with axios and renders a blob/object URL.

### Accessibility (WCAG 2.1 AA)

A reusable a11y toolkit lives in [frontend/src/components/a11y/](frontend/src/components/a11y/):
`AccessibleModal` (the single dialog wrapper — focus trap, Esc-to-close, backdrop
dismiss, dialog role/`aria-modal`/label, Android back; every overlay renders through
it), `IconButton` (required accessible `label` + role + state + visible focus ring for
every glyph button), `useAnnouncer` (an app-level live region — `announce(...)` speaks
highlights/chapter-changes/lookup results politely and errors assertively),
`SkipToContent` (the keyboard "skip to main content" link), plus `useFocusTrap`,
`useEscToClose`, and `focusStyles`. The chapter WebView document also carries
`<html lang>`, a `prefers-reduced-motion` block, a `role="document"` landmark, and
ARIA-labelled `<mark>` highlights. Because the app runs on web *and* native, primitives
**dual-write**: the RN `accessibility*` prop plus raw ARIA under a `Platform.OS === 'web'`
guard. Full tutorial: [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md).

### Reference lookup (dictionary & Wikipedia)

Selecting text in the reader and tapping **Look up** opens
[LookupOverlay](frontend/src/components/LookupOverlay.tsx) — a transparent bottom-sheet
(so the reader stays mounted and the scroll position survives) with two independent
sections: an **offline** WordNet dictionary and an **online** Wikipedia summary, fetched
by two `enabled`-gated, 5-minute-cached React Query hooks
([useLookup](frontend/src/hooks/useLookup.ts)). Each section renders a four-way
**loading / error / found / not-found** matrix — "not found" is a distinct friendly
state, never an error, mirroring the backend's 200-with-`found`-flag contract. Triggered
from [SelectionMenu](frontend/src/components/SelectionMenu.tsx). Full tutorial (backend
to UI): [docs/LOOKUP.md](docs/LOOKUP.md).

---

## 9. Testing

### Backend — xUnit (`backend/EReader.Tests/`)

Stack: xUnit · FluentAssertions · Moq · EF Core InMemory · coverlet. Coverage
focuses on the **service layer** (the controllers are dumb mappers, the
repositories are dumb EF wrappers). `EReader.Core` declares
`[assembly: InternalsVisibleTo("EReader.Tests")]` so tests can reach internal
helpers (`BookService.EncodeCursor`, `CredentialValidator`, `SearchService`
cursor codecs).

```bash
cd backend
dotnet test                                          # all tests
dotnet test --filter "FullyQualifiedName~AuthService"  # one class
dotnet test --collect:"XPlat Code Coverage"          # coverage
```

Covered: auth flows (incl. reuse detection, inactive users, constant-message
failure), ingestion (hashing, dedup, orphan cleanup, file-type validation), book
service (cursor codec, prev/next, 404-on-foreign-user), users (rename conflicts,
password change ± revoke-others), reading settings, asset URL rewriting, search,
and a `BookRepository` integration test.

### Frontend — Jest + RNTL

```bash
cd frontend
npm test                # jest
npm run test:watch
npm run test:coverage
```

Query by role/text/label — never by test ID. Test hooks with `renderHook`.

### Frontend — Playwright (web E2E)

```bash
cd frontend
npm run test:e2e:web       # headless; auto-starts the Expo web server
npm run test:e2e:web:ui    # interactive
```

### Frontend — Detox (native E2E) — **not active**

Requires `npx expo prebuild` to generate `ios/`/`android/` first. Scripts:
`test:e2e:ios:build` / `test:e2e:ios` (and android equivalents).

---

## 10. Conventions & house rules

These are enforced (review + the `review-pr` standard). Full text in
[best-practices.md](best-practices.md).

**API**
- All routes under `/api/v1/...`; plural resource nouns, no verbs in paths
  (auth + change-password are the documented action-style carve-outs).
- All errors use `{ error: { code, message, details } }`.
- All list endpoints are paginated (cursor preferred); never return unbounded
  collections.

**C# / .NET**
- Controllers are thin: validate → call service → map to HTTP. **No business
  logic or DB access in controllers.**
- Every async controller action takes a `CancellationToken` and threads it through
  every downstream call.
- Truly async end-to-end — no `.Result` / `.Wait()`; `async void` is banned
  outside event handlers.
- Register dependencies by **interface**, never concrete type. Pick lifetimes
  intentionally (scoped for DB-touching, transient for stateless helpers,
  singleton for thread-safe shared state).
- Never catch `Exception` broadly to swallow — let it bubble to the global
  middleware.

**TypeScript / React**
- **No `any`** — use `unknown` when the shape is truly unknown.
- One function component per file; co-locate single-use types, extract shared ones
  to `types/`.
- **Data fetching lives in hooks**, not component bodies.
- Context is for cross-cutting concerns (auth, theme) — **not** server state.

**Testing**
- Names follow `Should_[Result]_When_[Condition]`; each test is self-contained
  (arranges its own state, cleans up). Don't test implementation details.

**Git**
- ⚠️ **Per [CLAUDE.md](CLAUDE.md), automated agents must never `git commit`** —
  they may stage and draft a message; a human commits.
- Branches: `feat/…`, `fix/…`; PRs into `master`.

---

## 11. Common tasks (runbook)

**Add an API endpoint**
1. Define/extend the interface in `EReader.Core/Interfaces/`.
2. Implement business logic in `EReader.Core/Services/` (throw domain exceptions,
   take a `CancellationToken`).
3. DB access in `EReader.Data/Repositories/`.
4. Register in `Program.cs` by interface.
5. Add a thin controller action + DTOs in `EReader.Api/`.
6. Unit-test the service.

**Add a migration**
```bash
cd backend
dotnet ef migrations add <Name> --project EReader.Data --startup-project EReader.Api
```
Dev auto-applies on next `dotnet run`. ⚠️ If the migration touches
`Users.Username` (or any `_Lower` index), **hand-edit** the generated
`CreateIndex` into a `CREATE UNIQUE INDEX … LOWER(...)` (see §12).

**Add a frontend screen**
1. Create the route file under `app/` (under `(authed)/` if it needs a session).
2. Put data fetching in a `src/hooks/` hook over React Query, with a wrapper in
   `src/services/`.
3. Build the screen in `src/screens/`, shared UI in `src/components/`.

**Reset local DB**
```bash
docker compose --env-file docker/.env -f docker/docker-compose.yml down -v   # drops Postgres + Redis + MinIO volumes
docker compose --env-file docker/.env -f docker/docker-compose.yml up -d      # migrations re-run + bucket re-created on next dotnet run
```

---

## 12. Known sharp edges

- **Functional index on `Users.Username`.** EF will silently downgrade the
  `LOWER(username)` unique index to a plain one on any migration that touches that
  column. Always inspect generated migrations for `IX_Users_Username_Lower` (or
  any `_Lower` index) and restore the raw `CREATE UNIQUE INDEX … LOWER(...)` form.
- **Two `.env` files, split by consumer.** `docker/.env` feeds the *container
  stack* (`DB_PASSWORD`, `MINIO_USER/PASSWORD/BUCKET/ENDPOINT`);
  `backend/EReader.Api/.env` feeds the *API* (`DATABASE_URL`, `JWT__KEY`,
  `REDIS__CONNECTIONSTRING`). The `DB_PASSWORD` in the former must match the
  `Password=` embedded in `DATABASE_URL` in the latter, and Postgres bakes it into
  its data volume on first `up` — change it and you must `down -v` to re-init.
- **MinIO creds aren't in the backend template.** The API requires `MINIO_USER`
  and `MINIO_PASSWORD` (throws at boot otherwise), but `backend/EReader.Api/.env.example`
  doesn't ship them — copy the `MINIO_*` values from `docker/.env` into the
  backend `.env` (or export them) before `dotnet run` (§4 Step 2).
- **Docs, multiple altitudes.** This README is the front door (setup, API surface,
  high-level map). [docs/CODE_OVERVIEW.md](docs/CODE_OVERVIEW.md) is the deep-dive:
  §3–11 for backend internals, and §12 is a full frontend walk-through written for
  engineers new to React Native (§12.10–12.11 orient on a11y + lookup). Two feature
  areas get their own tutorial-style docs: [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md)
  and [docs/LOOKUP.md](docs/LOOKUP.md). Keep them in sync when behavior changes.
- **EPUBs are buffered into memory on both ends.** Upload buffers the whole file
  to hash + persist; serving an asset/cover pulls the object back from MinIO into a
  `MemoryStream` (the zip reader needs random access). Fine for the current few-MB
  books; revisit if large files arrive.
- **Search snippets contain raw `<mark>` HTML** — sanitize before rendering.
- **Production CORS and production migrations are intentionally NOT configured** —
  dev-only CORS is in `Program.cs`, and migrations auto-apply only in Development.
  Both must be handled deliberately per-environment at deploy time.
```

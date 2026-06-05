# EReader — Reference Lookup (Dictionary & Wikipedia) Deep-Dive

A full walk-through of the **look-up-a-word/term** feature: select text in the reader,
tap **Look up**, and get an offline dictionary definition plus an online Wikipedia
summary in one overlay. This is written as a **tutorial for an engineer who has never
built a lookup feature before** — it teaches the design decisions (offline vs online
data, why the endpoints never 404 on "not found", caching, the typed `HttpClient`
pattern) as it walks the actual code, end to end, backend and frontend.

> Companion to [CODE_OVERVIEW.md](CODE_OVERVIEW.md) (the backend internals + frontend
> deep-dive) and [ACCESSIBILITY.md](ACCESSIBILITY.md) (the overlay is built on the
> a11y primitives described there). Read CODE_OVERVIEW §12.0 first if React Native is
> new to you.

The feature satisfies the project's reference-lookup requirements (cited in code as
**FR-25** dictionary, **FR-26** "found" handling, **FR-27** Wikipedia, **FR-28**
return to reading position).

---

## 0. The shape of the feature (mental model)

There are **two independent data sources**, deliberately different in nature:

| Source | Where the data lives | Network? | Why this choice |
|---|---|---|---|
| **Dictionary** | A WordNet dataset shipped *inside the backend* and loaded into memory | **No** — fully offline | Definitions are static, small, and you want instant, reliable lookups even with no/poor connectivity |
| **Wikipedia** | Wikipedia's public REST API, proxied through the backend | **Yes** — live call | Encyclopaedic summaries are huge, change constantly, and can't be bundled |

Both are exposed as two small backend endpoints under `/api/v1/lookup`, fetched by two
React Query hooks, and rendered stacked in one overlay. The data flow:

```
 Reader: user selects "photosynthesis"
        │  (SelectionMenu → "Look up")
        ▼
 LookupOverlay term="photosynthesis"
        ├── useDictionary(term) ── GET /api/v1/lookup/define?word=photosynthesis ──► DictionaryService (in-memory WordNet)
        └── useWikipedia(term)  ── GET /api/v1/lookup/wikipedia?term=photosynthesis ─► WikipediaService ──► en.wikipedia.org REST
```

The single most important design idea to absorb up front:

> **"No definition found" / "no article" is a normal, successful outcome — not an
> error.** Both endpoints return **HTTP 200** with a `found: boolean` in the body.
> They never 404. (A missing *word* is not a missing *endpoint*.)

That one decision shapes the controller, the service, the DTO, and the UI states, so
keep it in mind throughout.

---

## 1. The dictionary data — building an offline dataset

Bundling a dictionary means turning a public lexical database into a small file the
app can load fast.

- **Source:** WordNet (a standard open lexical database of English).
- **Build script:** [scripts/build-dictionary.sh](../scripts/build-dictionary.sh)
  emits `wordnet.json.gz`.
- **On-disk format:** a single JSON object mapping `word → [ { pos, definition,
  examples[] } ]`, gzipped. The committed artifact lives at
  [backend/EReader.Api/data/dictionary/wordnet.json.gz](../backend/EReader.Api/data/dictionary/)
  and is **~6.6 MB gzipped** (vs ~28 MB raw JSON).

The gzip choice matters: it's checked into git, so keeping it at 6.6 MB instead of
28 MB is the difference between a reasonable and an obnoxious repo. The cost is paid
once, at startup, decompressing into memory.

`pos` = *part of speech* ("noun", "verb", …). A word has multiple **senses** (distinct
meanings), each with its own part of speech, definition, and example sentences — which
is why the value is an array, not a single definition.

---

## 2. `DictionaryService` — loading and querying the offline index

[backend/EReader.Core/Services/DictionaryService.cs](../backend/EReader.Core/Services/DictionaryService.cs)

### 2.1 Loading the dataset once, at startup

The service is registered as a **singleton** (see §4), so its constructor runs once
for the app's lifetime and builds an in-memory index:

```csharp
using var file = File.OpenRead(path);
using var gzip = new GZipStream(file, CompressionMode.Decompress);
var raw = JsonSerializer.Deserialize<Dictionary<string, List<RawSense>>>(gzip, JsonOpts) ?? [];
```

Two things worth calling out for a newcomer:

- **It streams through the `GZipStream` directly into `JsonSerializer`.** The ~28 MB
  of decompressed JSON is *never* fully materialised as a `byte[]` or a `string` first
  — the serializer pulls bytes through the decompression stream as it parses. For a
  big asset like this, streaming vs buffering is the difference between a brief
  startup blip and a 28 MB allocation spike.
- **The index is keyed case-insensitively:** `ToDictionary(…,
  StringComparer.OrdinalIgnoreCase)`. So "Photosynthesis" and "photosynthesis" hit the
  same entry without you lower-casing on every lookup.

The path is resolved relative to `AppContext.BaseDirectory` (the running binary's
folder), and the dataset is copied next to the binary on build — which is why you see
`data/dictionary/wordnet.json.gz` under both `bin/Debug/.../` and the source `data/`.

### 2.2 The `RawSense` vs `DictionarySense` split

You'll notice two near-identical types. `RawSense` is a private record whose
`[JsonPropertyName("pos")]` etc. mirror the **short keys in the on-disk JSON**;
`DictionarySense` (in `EReader.Core/Lookups`) is the clean domain type with full names
(`PartOfSpeech`). The loader maps one to the other. Why bother? It **decouples the
storage format from the domain model** — the JSON uses `"pos"` to save bytes ×
millions of entries, but the rest of the code shouldn't have to know or care. If the
dataset format changes, only `RawSense` and the mapping change.

### 2.3 Querying — normalisation and the "not found" contract

```csharp
public DictionaryResult Lookup(string word)
{
    if (string.IsNullOrWhiteSpace(word)) throw new ValidationException("A word is required.");
    var key = Normalize(word);
    if (key.Length > MaxWordLength) throw new ValidationException("Word is too long.");
    return _index.TryGetValue(key, out var senses)
        ? new DictionaryResult(key, true, senses)
        : new DictionaryResult(key, false, []);   // ← found:false, NOT an exception
}
```

- **Validation throws** (empty word → 400, over 100 chars → 400). These are genuinely
  bad *requests*.
- **A missing word does not throw.** It returns `DictionaryResult(key, found: false,
  [])`. The record's own comment states it: *"Found=false is a normal outcome (FR-26),
  not an error — we do NOT throw NotFoundException here."* This is the §0 contract in
  code.

**`Normalize`** handles the reality that the user selected *prose*, not a clean lemma.
A selection might be `"Photosynthesis,"` or even a phrase. The normaliser takes the
**first whitespace-delimited token**, strips surrounding punctuation
(`. , ; : " ' ( ) ! ?`), and lower-cases it. So selecting `"photosynthesis."` or
`"photosynthesis is"` both resolve to `photosynthesis`. Full lemmatisation (mapping
"running" → "run") is explicitly out of scope for v1 — a documented limitation, not a
bug.

---

## 3. `WikipediaService` — proxying a live API with a typed `HttpClient`

[backend/EReader.Core/Services/WikipediaService.cs](../backend/EReader.Core/Services/WikipediaService.cs)

This is the codebase's **first use of `IHttpClientFactory` / typed `HttpClient`**, so
it's a good template to learn from.

```csharp
public WikipediaService(HttpClient http) => _http = http;

public async Task<WikipediaResult> GetSummaryAsync(string term, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(term)) throw new ValidationException("A term is required.");
    var title = Uri.EscapeDataString(term.Trim().Replace(' ', '_'));
    using var resp = await _http.GetAsync($"page/summary/{title}", ct);
    if (resp.StatusCode == HttpStatusCode.NotFound)
        return new WikipediaResult(term, false, null, null, null, null);   // ← found:false
    resp.EnsureSuccessStatusCode();
    …
}
```

What's going on, decision by decision:

- **The `HttpClient` is injected, not `new`ed.** Manually `new HttpClient()`ing in a
  service is a classic .NET footgun (socket exhaustion under load). The DI container
  supplies a pooled, configured client (base address, headers, timeout — see §4). The
  service just makes requests against a relative path (`page/summary/{title}`).
- **The Wikipedia REST URL shape:** `…/page/summary/{Title}` where the title is the
  page title with spaces → underscores, then URL-encoded (`Uri.EscapeDataString`).
- **404 from Wikipedia → `found: false`, 200 to our client.** Same contract as the
  dictionary: "no such article" is a normal answer, *not* an error we propagate. Any
  *other* non-success status *does* throw (`EnsureSuccessStatusCode`) and bubbles to
  the global error middleware as a 500 — a real upstream failure is different from "no
  article".
- **`CancellationToken ct` is threaded** through `GetAsync`, `ReadAsStreamAsync`, and
  `ParseAsync`. If the client disconnects, the upstream call is abandoned promptly.

### 3.1 Defensive JSON parsing

Wikipedia's summary payload is large and not every field is always present. Rather
than deserialize into a fixed C# class (which would be brittle), the service parses
into a `JsonDocument` and reads fields *defensively* with local helpers:

```csharp
string? Str(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
string? PageUrl() => root.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var d) && d.TryGetProperty("page", out var pg) ? pg.GetString() : null;
string? Thumb()   => root.TryGetProperty("thumbnail", out var t) && t.TryGetProperty("source", out var s) ? s.GetString() : null;
```

Every access is a `TryGetProperty` with a null fallback — a missing `extract`,
`content_urls`, or `thumbnail` yields `null`, never an exception. It pulls just four
fields it cares about (`title`, `extract`, the desktop page URL, the thumbnail) and
ignores the rest of the (large) payload. This is the right posture for any external
JSON you don't control: **read what you need, tolerate anything missing.**

---

## 4. Wiring it up — DI registration (the contrast is the lesson)

[backend/EReader.Api/Program.cs](../backend/EReader.Api/Program.cs)

The two services are registered very differently, and *why* is instructive:

```csharp
// Dictionary: SINGLETON — dataset loaded once into memory at startup.
builder.Services.AddOptions<DictionaryOptions>()
    .Bind(builder.Configuration.GetSection(DictionaryOptions.SectionName));
builder.Services.AddSingleton<IDictionaryService>(sp =>
    new DictionaryService(sp.GetRequiredService<IOptions<DictionaryOptions>>().Value));

// Wikipedia: typed HttpClient (first IHttpClientFactory use in the codebase).
builder.Services.AddHttpClient<IWikipediaService, WikipediaService>(client =>
{
    var baseUrl = builder.Configuration["WikipediaApiBase"] ?? "https://en.wikipedia.org/api/rest_v1";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EReader/1.0 (coding-challenge-109)");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

- **Dictionary = singleton.** It holds a large, immutable in-memory index that's
  expensive to build. You want exactly one, alive for the whole process. The index is
  read-only after construction, so it's safe to share across all concurrent requests.
- **Note the `IOptions<>` unwrap.** `DictionaryService` takes a *plain*
  `DictionaryOptions`, not `IOptions<DictionaryOptions>`, because `EReader.Core` has
  **no framework package references by design** (a hard architectural rule — Core
  stays pure). So the API layer binds the options and unwraps `.Value` before passing
  it in. This is the standard way Core services receive configuration in this repo.
- **Wikipedia = typed `HttpClient`.** `AddHttpClient<IWikipediaService,
  WikipediaService>` registers the service *and* configures a pooled `HttpClient` for
  it. The config sets the **base address** (so the service uses relative paths),
  a **`User-Agent`** (Wikipedia's API requires one and will reject anonymous
  requests), and a **10s timeout** (don't let a slow upstream hang a request forever).
  The base URL is overridable via the `WikipediaApiBase` config key — handy for
  pointing tests at a stub.

`DictionaryOptions.DataPath` defaults to `data/dictionary/wordnet.json.gz` and is
similarly overridable via the `Dictionary` config section.

---

## 5. The HTTP boundary — controller & DTOs

[backend/EReader.Api/Controllers/LookupController.cs](../backend/EReader.Api/Controllers/LookupController.cs)

The controller is thin, as the house rules demand — validate-free here (the services
validate), just call and map:

```csharp
[HttpGet("define")]
public ActionResult<DictionaryResponse> Define([FromQuery] string word)
    => Ok(DictionaryResponse.From(_dictionary.Lookup(word)));        // synchronous — in-memory

[HttpGet("wikipedia")]
public async Task<ActionResult<WikipediaResponse>> Wikipedia([FromQuery] string term, CancellationToken ct)
    => Ok(WikipediaResponse.From(await _wikipedia.GetSummaryAsync(term, ct)));   // async — network
```

Details worth noting:

- **`Define` is synchronous; `Wikipedia` is async.** The dictionary is a pure
  in-memory dictionary lookup — there's nothing to await, so making it `async` would
  be ceremony. Wikipedia does real I/O, so it's `async` and takes a
  `CancellationToken`. (This is a nice illustration of "truly async only where there's
  actually I/O".)
- **Both return `Ok(...)` unconditionally.** No `NotFound()` branch anywhere — the
  `found` flag lives in the body. The §0 contract, enforced at the boundary.
- **These endpoints require auth** like everything else (the global fallback
  `AuthorizeFilter` from `Program.cs`; they don't opt out with `[AllowAnonymous]`).

The DTOs ([LookupResponses.cs](../backend/EReader.Api/Dtos/LookupResponses.cs)) are
the usual wire-shape records with a static `From(domainResult)` mapper, keeping the
domain types (`DictionaryResult`, `WikipediaResult` in `EReader.Core/Lookups`)
separate from the JSON contract. The response shapes:

```
DictionaryResponse { word, found, senses: [ { partOfSpeech, definition, examples[] } ] }
WikipediaResponse  { term, found, title, extract, pageUrl, thumbnailUrl }
```

These serialize to camelCase and are mirrored exactly by the TypeScript types in
[frontend/src/types/index.ts](../frontend/src/types/index.ts) (`DictionaryResult`,
`DictionarySense`, `WikipediaResult`).

---

## 6. The frontend — service wrappers and hooks

### 6.1 Thin endpoint wrappers

[frontend/src/services/lookup.ts](../frontend/src/services/lookup.ts) — two one-line
functions over the shared axios instance (which carries the auth interceptor, so these
inherit the Bearer token automatically):

```ts
export const defineWord = (word: string) =>
  api.get<DictionaryResult>('/api/v1/lookup/define', { params: { word } }).then(r => r.data);
export const getWikipediaSummary = (term: string) =>
  api.get<WikipediaResult>('/api/v1/lookup/wikipedia', { params: { term } }).then(r => r.data);
```

Per the project's data-fetching rule, services are *only* the HTTP wrapper; the
React-Query orchestration is one layer up.

### 6.2 The React Query hooks

[frontend/src/hooks/useLookup.ts](../frontend/src/hooks/useLookup.ts):

```ts
export function useDictionary(word: string | null) {
  return useQuery({
    queryKey: ['lookup', 'define', word],
    queryFn: () => defineWord(word!),
    enabled: !!word,
    staleTime: 5 * 60_000,
  });
}
// useWikipedia is identical with key ['lookup', 'wikipedia', term].
```

Three React Query patterns doing real work here:

- **`enabled: !!word`** — the query is *disabled* until there's a term. The overlay can
  mount/initialise without firing a request for `null`; the moment a term exists it
  runs. (The `word!` non-null assertion is safe precisely because `enabled` gates it.)
- **`staleTime: 5 minutes`** — a looked-up word stays "fresh" for 5 minutes, so
  re-opening the same word (very common — you forget a definition and check again)
  serves instantly from cache with **no network call**. The dictionary is offline-fast
  anyway, but this also spares Wikipedia repeat hits.
- **Structured query keys** (`['lookup', 'define', word]`) — each word is cached
  separately and React Query dedupes concurrent identical lookups for free.

The two hooks are independent: dictionary and Wikipedia resolve, cache, load, and fail
on their own timelines. That independence is what lets the overlay show one section
loaded while the other is still spinning or has errored.

---

## 7. `LookupOverlay` — rendering the result

[frontend/src/components/LookupOverlay.tsx](../frontend/src/components/LookupOverlay.tsx)

A bottom-sheet overlay with two stacked sections (Dictionary on top, Wikipedia below),
each driven by its own hook. It's built on the accessibility primitives — read
[ACCESSIBILITY.md](ACCESSIBILITY.md) for those; here's what's lookup-specific.

### 7.1 Why a transparent modal (FR-28: never lose the reading position)

```tsx
<AccessibleModal visible onClose={onClose} label={`Lookup: ${term}`}
                 animationType="slide" align="bottom" panelStyle={…}>
```

It renders through `AccessibleModal` as a **bottom-aligned, transparent** sheet. The
point: the reader stays mounted and untouched behind it, so dismissing the overlay
returns the user to the *exact* scroll position they were reading — looking up a word
must never cost you your place. (Contrast with navigating to a separate lookup screen,
which would unmount/remount the reader and lose position.)

### 7.2 The state matrix — every section handles four states

Each section explicitly renders **loading / error / found / not-found**:

```tsx
{dictionary.isLoading ? <ActivityIndicator/>
  : dictionary.isError ? <Text>Couldn't load the dictionary.</Text>
  : dictionary.data?.found ? <DictionarySenses senses={dictionary.data.senses}/>
  : <Text>No definition found for "{…}".</Text>}
```

This is the §0 contract surfacing in the UI: **`found: false` is a distinct, friendly
state** ("No definition found"), *separate* from `isError` ("Couldn't load the
dictionary" — an actual request failure). New engineers often collapse these two; keep
them apart. The Wikipedia section mirrors the same four-way branch, plus it
conditionally shows the title, the extract, and a **"Read on Wikipedia"** link
(`Linking.openURL(pageUrl)`) when present.

`DictionarySenses` is a small sub-component that lists each sense with its part of
speech and any example sentences — pulled out just to keep the main render readable.

### 7.3 Accessibility specifics (the part unique to lookup)

A lookup resolves *without* any visible focus change, which is exactly the situation
live regions exist for (see [ACCESSIBILITY.md](ACCESSIBILITY.md) §7). The overlay
announces the **outcome** so a screen-reader user hears the result of a lookup they
can't watch resolve:

```tsx
useEffect(() => {
  if (dictionary.isLoading) return;
  if (dictionary.isError) { announce('Could not load the dictionary', true); return; }  // assertive
  if (dictionary.data)
    announce(dictionary.data.found ? `Definition found for ${term}` : `No definition found for ${term}`);
}, [dictionary.isLoading, dictionary.isError, dictionary.data, term, announce]);
```

Note the "no definition" case is announced too — silence would leave the user
wondering if the lookup even ran. Errors go out **assertive** (interrupt); the
found/not-found result goes out **polite**. The section labels and term use
`accessibilityRole="header"`, and the "Read on Wikipedia" control uses
`accessibilityRole="link"` because it navigates out of the app.

---

## 8. The trigger — from text selection to overlay

[frontend/src/components/SelectionMenu.tsx](../frontend/src/components/SelectionMenu.tsx)
and [ReaderScreen.tsx](../frontend/src/screens/ReaderScreen.tsx)

The full chain that gets a word into the overlay:

1. **Inside the chapter WebView**, the injected script
   ([lib/webviewScripts.ts](../frontend/src/lib/webviewScripts.ts)) listens for
   `mouseup`/`touchend`/Shift+Arrow and, on a non-empty selection, posts a `selection`
   message to the host carrying the selected text and its on-screen rectangle.
2. **`ReaderScreen`** receives it and stores `selection` (text + anchor + rect). That
   renders **`SelectionMenu`** — a small popover anchored above the selection rect
   offering highlight colours, Add note, Bookmark, and **Look up**.
3. **Tapping "Look up"** runs `onLookup`: it sets `lookupTerm = selection.selectedText`
   and clears the selection (closing the popover). `lookupTerm` being non-null mounts
   `<LookupOverlay term={lookupTerm} onClose={() => setLookupTerm(null)} />`.
4. The overlay's two hooks fire against the two endpoints; sections fill in
   independently.

So the term handed to the backend is the **raw selected prose** — which is exactly why
the backend's `DictionaryService.Normalize` (§2.3) has to strip punctuation and take
the first token. The frontend doesn't pre-clean it; normalisation is the server's job,
keeping the client dumb and the rule in one place.

`SelectionMenu` itself is a nice bit of UI: it measures its own size on first layout
and clamps all four edges against the viewport so the popover never renders
off-screen, preferring to sit above the selection but dropping below it when there's
no room.

---

## 9. End-to-end trace (one lookup, both halves)

`User selects "Gutenberg." and taps Look up`:

```
WebView selection message ─► ReaderScreen sets lookupTerm="Gutenberg."
   │
   ▼  <LookupOverlay term="Gutenberg.">
   ├─ useDictionary("Gutenberg.")
   │     GET /api/v1/lookup/define?word=Gutenberg.
   │     → DictionaryService.Normalize → "gutenberg" → index miss
   │     → 200 { word:"gutenberg", found:false, senses:[] }
   │     → overlay: "No definition found for 'gutenberg'."  + polite announce
   │
   └─ useWikipedia("Gutenberg.")
         GET /api/v1/lookup/wikipedia?term=Gutenberg.
         → WikipediaService: title "Gutenberg." → GET …/page/summary/Gutenberg.
         → 200 JSON → { found:true, title:"Johannes Gutenberg", extract:"…", pageUrl:"…" }
         → overlay: title + extract + "Read on Wikipedia"
```

Both calls returned **200** even though the dictionary "failed" to find the word — the
hallmark of this feature. The two sections resolved independently: a dictionary miss
sits happily next to a Wikipedia hit.

---

## 10. Design decisions recap & how to extend

**Why this shape:**

- **Offline dictionary, online Wikipedia** — match the storage strategy to the data:
  small + static + latency-sensitive → bundle it; huge + live → proxy it.
- **Endpoints never 404 on "not found"** — a missing word/article is data, not an
  error; it rides in a `found` boolean so the UI can show a friendly, distinct state.
- **Singleton in-memory index vs typed `HttpClient`** — the two correct-but-opposite
  DI lifetimes for "load once, read forever" vs "pooled network client".
- **Normalisation on the server** — the client sends raw selected prose; the server
  owns the one place that cleans it.
- **5-minute client cache + structured keys** — re-looking-up a word is instant and
  free.
- **Transparent bottom-sheet overlay** — reading position is sacred (FR-28).

**To add a third source** (say, a thesaurus): add an interface + service in
`EReader.Core` (throw `ValidationException` for bad input, return a `found`-style
result for misses — don't throw on "not found"), register it in `Program.cs` with the
lifetime that fits its data (singleton for bundled, typed `HttpClient` for remote),
add a thin controller action returning `Ok(...)` unconditionally + a DTO, mirror the
type in `types/index.ts`, add a service wrapper + a `useThesaurus` hook
(`enabled`-gated, `staleTime`'d), and render a third section in `LookupOverlay` with
the same loading/error/found/not-found matrix and an `announce` on resolve.

### Tests

- Frontend: [useLookup.test.tsx](../frontend/src/hooks/__tests__/useLookup.test.tsx)
  (hook behaviour) and
  [LookupOverlay.test.tsx](../frontend/src/components/__tests__/LookupOverlay.test.tsx)
  (the state matrix + the a11y announcements).
- Backend: the dictionary normalisation/"not found" contract and the Wikipedia 404 →
  `found:false` mapping are the key behaviours to cover (the `WikipediaApiBase` config
  override exists so you can point the typed client at a stub server in tests).

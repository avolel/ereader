using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Lookups;

namespace EReader.Core.Services;

public sealed class DictionaryOptions
{
    public const string SectionName = "Dictionary";

    // Gzipped dataset emitted by scripts/build-dictionary.sh (~6.6 MB vs ~28 MB raw).
    public string DataPath { get; set; } = "data/dictionary/wordnet.json.gz";
}

public sealed class DictionaryService : IDictionaryService
{
    private const int MaxWordLength = 100;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DictionarySense>> _index;

    // Plain DictionaryOptions (not IOptions<>): EReader.Core has no framework package
    // references by design, so the API layer binds configuration and passes the value in.
    public DictionaryService(DictionaryOptions options)
    {
        var path = Path.Combine(AppContext.BaseDirectory, options.DataPath);

        // Dataset is committed gzipped; decompress on the way into the in-memory index.
        // Streamed (not read into a byte[] first) so the ~28 MB of JSON never fully
        // materializes — JsonSerializer pulls through the GZipStream as it parses.
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<RawSense>>>(gzip, JsonOpts) ?? [];

        _index = raw.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DictionarySense>)kv.Value
                .Select(s => new DictionarySense(s.Pos, s.Definition, s.Examples ?? []))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    public DictionaryResult Lookup(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            throw new ValidationException("A word is required.");

        var key = Normalize(word);
        if (key.Length > MaxWordLength)
            throw new ValidationException("Word is too long.");

        return _index.TryGetValue(key, out var senses)
            ? new DictionaryResult(key, true, senses)
            : new DictionaryResult(key, false, []);
    }

    // Selection may be a phrase; dictionary keys are single lemmas. Take the first token,
    // strip surrounding punctuation, lower-case. (Lemmatization is out of scope for v1.)
    private static string Normalize(string word) =>
        word.Trim().Split(' ', '\t', '\n')[0]
            .Trim('.', ',', ';', ':', '"', '\'', '(', ')', '!', '?')
            .ToLowerInvariant();

    // Mirrors the on-disk JSON shape (short keys) emitted by scripts/build-dictionary.sh.
    // Kept private so DictionarySense stays decoupled from the storage format — the JSON
    // key "pos" would not bind to DictionarySense.PartOfSpeech by case-insensitive matching.
    private sealed record RawSense(
        [property: JsonPropertyName("pos")] string Pos,
        [property: JsonPropertyName("definition")] string Definition,
        [property: JsonPropertyName("examples")] IReadOnlyList<string>? Examples);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
using System.Net;
using System.Text.Json;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Lookups;

namespace EReader.Core.Services;

public class WikipediaService : IWikipediaService
{
    private readonly HttpClient _http;

    public WikipediaService(HttpClient http) => _http = http;

    public async Task<WikipediaResult> GetSummaryAsync(string term, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new ValidationException("A term is required.");

        var title = Uri.EscapeDataString(term.Trim().Replace(' ', '_'));
        using var resp = await _http.GetAsync($"page/summary/{title}", ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return new WikipediaResult(term, false, null, null, null, null);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        string? Str(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string? PageUrl() =>
            root.TryGetProperty("content_urls", out var cu) &&
            cu.TryGetProperty("desktop", out var d) &&
            d.TryGetProperty("page", out var pg) ? pg.GetString() : null;
        string? Thumb() =>
            root.TryGetProperty("thumbnail", out var t) && t.TryGetProperty("source", out var s) ? s.GetString() : null;

        return new WikipediaResult(term, true, Str("title"), Str("extract"), PageUrl(), Thumb());
    } 
}

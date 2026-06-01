using System.Text.RegularExpressions;
using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using VersOne.Epub;
using VersOne.Epub.Schema;

namespace EReader.Data.Parsing;

public sealed partial class EpubParserAdapter : IEpubParser
{
    public async Task<ParsedEpub> ParseAsync(Stream epubStream, CancellationToken ct)
    {
        EpubBook book;
        try
        {
            book = await EpubReader.ReadBookAsync(epubStream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // VersOne throws a variety of internal exceptions on malformed/missing
            // OPF, broken XML, etc. — collapse them all into the domain exception
            // so the controller can map this to a 422.
            throw new MalformedEpubException("EPUB could not be parsed.", ex);
        }

        ct.ThrowIfCancellationRequested();

        var metadata = book.Schema?.Package?.Metadata;
        var language = metadata?.Languages.FirstOrDefault()?.Language;
        var publisher = metadata?.Publishers.FirstOrDefault()?.Publisher;
        var publishedDate = PickPublishedDate(metadata);
        var publishedYear = ExtractYear(publishedDate);

        ParsedCover? cover = null;
        if (book.Content?.Cover is { } coverFile && coverFile.Content is { Length: > 0 })
        {
            cover = new ParsedCover(coverFile.Content, ExtensionFromPath(coverFile.FilePath));
        }

        // Build a flat map from chapter FilePath → human title using the EPUB nav tree.
        // EPUB nav often points many entries at the same file (sub-headings via #anchor);
        // we keep the first occurrence, which is the section's top-level title.
        var titlesByPath = BuildTitleMap(book.Navigation);

        var chapters = new List<ParsedChapter>(book.ReadingOrder?.Count ?? 0);
        if (book.ReadingOrder is { } spine)
        {
            for (int i = 0; i < spine.Count; i++)
            {
                var item = spine[i];
                var title = titlesByPath.TryGetValue(item.FilePath, out var t) ? t : null;
                chapters.Add(new ParsedChapter(
                    SpineOrder: i,
                    Title: title,
                    ContentHref: item.FilePath,
                    ContentText: ExtractBody(item.Content ?? string.Empty)));
            }
        }

        return new ParsedEpub(
            Title: NonEmpty(book.Title) ?? "Untitled",
            Author: NonEmpty(book.Author) ?? "Unknown",
            Language: language,
            Publisher: publisher,
            PublishedDate: publishedDate,
            PublishedYear: publishedYear,
            Description: NonEmpty(book.Description),
            Cover: cover,
            Chapters: chapters);
    }

    private static string? PickPublishedDate(EpubMetadata? metadata)
    {
        if (metadata is null) return null;
        // Prefer the OPF date with event="publication" if present; otherwise first date.
        var pub = metadata.Dates.FirstOrDefault(d =>
            string.Equals(d.Event, "publication", StringComparison.OrdinalIgnoreCase));
        return NonEmpty(pub?.Date) ?? NonEmpty(metadata.Dates.FirstOrDefault()?.Date);
    }

    private static int? ExtractYear(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return null;
        var match = YearRegex().Match(rawDate);
        if (!match.Success) return null;
        return int.TryParse(match.Value, out var year) ? year : null;
    }

    private static Dictionary<string, string> BuildTitleMap(IList<EpubNavigationItem>? items)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return map;
        Walk(items, map);
        return map;

        static void Walk(IList<EpubNavigationItem> nodes, Dictionary<string, string> sink)
        {
            foreach (var node in nodes)
            {
                var path = node.Link?.ContentFilePath;
                if (!string.IsNullOrEmpty(path) && !sink.ContainsKey(path) && !string.IsNullOrWhiteSpace(node.Title))
                {
                    sink[path] = node.Title;
                }
                if (node.NestedItems is { Count: > 0 } children)
                {
                    Walk(children, sink);
                }
            }
        }
    }

    // Extracts the inner HTML of <body>...</body>. EPUBs are XHTML so a tag-based
    // slice is reliable enough; if no body is found we return the original content
    // rather than dropping it. The client renders this string inside its own host
    // element, so leaving the outer html/head out keeps it composable.
    internal static string ExtractBody(string xhtml)
    {
        if (string.IsNullOrEmpty(xhtml)) return string.Empty;

        var bodyStart = xhtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0) return xhtml;
        var openEnd = xhtml.IndexOf('>', bodyStart);
        if (openEnd < 0) return xhtml;
        var closeStart = xhtml.IndexOf("</body>", openEnd, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0) return xhtml[(openEnd + 1)..].Trim();
        return xhtml[(openEnd + 1)..closeStart].Trim();
    }

    private static string ExtensionFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return ".bin";
        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? ".bin" : ext;
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex YearRegex();
}

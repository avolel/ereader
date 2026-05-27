using System.Text.RegularExpressions;

namespace EReader.Core.Services;

// Rewrites relative src/href attributes inside a chapter's XHTML body so they
// point at the API's asset endpoint. Persisted ContentText keeps the original
// relative form — rewriting happens at read time so the DB stays decoupled
// from the API base URL.
public static partial class AssetUrlRewriter
{
    // chapterHref is the in-archive path of the chapter file
    // (e.g. "OEBPS/Text/chapter1.xhtml"). assetBaseUrl is the prefix to splice
    // onto each resolved asset path (e.g. "/api/v1/books/{id}/assets").
    public static string Rewrite(string content, string chapterHref, string assetBaseUrl)
    {
        if (string.IsNullOrEmpty(content)) return content;

        var chapterDirectory = GetDirectory(chapterHref);
        var baseUrl = assetBaseUrl.TrimEnd('/');

        return AttributeRegex().Replace(content, match =>
        {
            var attr = match.Groups["attr"].Value;
            var quote = match.Groups["quote"].Value;
            var url = match.Groups["url"].Value;

            if (!ShouldRewrite(url)) return match.Value;

            var resolved = ResolveRelative(chapterDirectory, url);
            return $"{attr}={quote}{baseUrl}/{resolved}{quote}";
        });
    }

    private static bool ShouldRewrite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        // Absolute URLs, fragment-only links, data: URIs, and protocol-relative
        // URLs are left alone — only true in-EPUB relative refs get rewritten.
        if (url.StartsWith('#')) return false;
        if (url.StartsWith("//", StringComparison.Ordinal)) return false;
        if (url.StartsWith('/')) return false;
        if (HasSchemeRegex().IsMatch(url)) return false;
        return true;
    }

    private static string ResolveRelative(string baseDir, string relative)
    {
        // Split off fragment/query so we can re-append after path normalization.
        var fragmentIndex = relative.IndexOfAny(['#', '?']);
        var suffix = fragmentIndex >= 0 ? relative[fragmentIndex..] : string.Empty;
        var pathPart = fragmentIndex >= 0 ? relative[..fragmentIndex] : relative;

        var combined = string.IsNullOrEmpty(baseDir) ? pathPart : $"{baseDir}/{pathPart}";
        return NormalizePath(combined) + suffix;
    }

    private static string NormalizePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.None);
        var stack = new List<string>(segments.Length);
        foreach (var raw in segments)
        {
            if (raw.Length == 0 || raw == ".") continue;
            if (raw == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(raw);
        }
        return string.Join('/', stack);
    }

    private static string GetDirectory(string href)
    {
        if (string.IsNullOrEmpty(href)) return string.Empty;
        var lastSlash = href.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : href[..lastSlash];
    }

    [GeneratedRegex(@"(?<attr>\b(?:src|href))\s*=\s*(?<quote>[""'])(?<url>[^""']*)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:")]
    private static partial Regex HasSchemeRegex();
}

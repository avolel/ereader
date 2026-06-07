using System.Text.RegularExpressions;

namespace EReader.Api.Auth;

// Native WebView/<Image> subresource requests (in-chapter <img> and the cover
// image) can't attach an `Authorization: Bearer` header. For those two media GET
// routes only, we additionally accept the access token via an `?access_token=`
// query param. This helper isolates the route-matching + extraction decision so
// it can be unit-tested without the ASP.NET request pipeline; JwtBearerSetup
// wires it into JwtBearerEvents.OnMessageReceived (the SignalR pattern).
public static partial class MediaQueryToken
{
    public const string QueryKey = "access_token";

    // The two routes whose media loads inside a WebView/<Image>:
    //   GET /api/v1/books/{id}/cover
    //   GET /api/v1/books/{id}/assets/{*assetPath}
    // Scoped deliberately so query-string tokens are never honoured elsewhere.
    [GeneratedRegex(@"^/api/v1/books/[^/]+/(?:cover|assets(?:/.*)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MediaPathRegex();

    public static bool IsMediaRoute(string? method, string? path)
    {
        if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path)) return false;
        if (!HttpMethods.IsGet(method)) return false;
        return MediaPathRegex().IsMatch(path);
    }

    // Returns the token the bearer handler should use from the query string, or
    // null to fall through to the standard Authorization-header extraction. The
    // query token is honoured only on the media GET routes above.
    public static string? ResolveQueryToken(string? method, string? path, string? queryToken)
    {
        if (string.IsNullOrEmpty(queryToken)) return null;
        return IsMediaRoute(method, path) ? queryToken : null;
    }
}

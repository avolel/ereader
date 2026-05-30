using System.Globalization;
using System.Text;
using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;

namespace EReader.Core.Services;

public sealed class SearchService : ISearchService
{
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 256;

    private readonly ISearchRepository _search;

    public SearchService(ISearchRepository search)
    {
        _search = search;
    }

    public async Task<SearchPage> SearchAsync(
        Guid userId,
        string query,
        Guid? bookFilter,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length < MinQueryLength)
        {
            throw new ValidationException($"Search query must be at least {MinQueryLength} characters.");
        }
        if (trimmed.Length > MaxQueryLength)
        {
            throw new ValidationException($"Search query must be no more than {MaxQueryLength} characters.");
        }

        var (cursorBookId, cursorSpineOrder) = DecodeCursor(cursor);

        var (rows, hasMore) = await _search.SearchAsync(
            userId,
            trimmed,
            bookFilter,
            cursorBookId,
            cursorSpineOrder,
            pageSize,
            ct);

        string? nextCursor = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            nextCursor = EncodeCursor(last.BookId, last.ChapterSpineOrder);
        }
        return new SearchPage(rows, nextCursor);
    }

    // Cursor: base64({bookId}:{spineOrder}). SpineOrder isn't sensitive and the
    // cursor format mirrors the repo's keyset (BookId, SpineOrder) ordering.
    internal static string EncodeCursor(Guid bookId, int spineOrder)
    {
        var payload = $"{bookId}:{spineOrder.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    internal static (Guid? BookId, int? SpineOrder) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2) throw new ValidationException("Invalid cursor.");
            if (!Guid.TryParse(parts[0], out var bookId)) throw new ValidationException("Invalid cursor.");
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var spine))
            {
                throw new ValidationException("Invalid cursor.");
            }
            return (bookId, spine);
        }
        catch (FormatException)
        {
            throw new ValidationException("Invalid cursor.");
        }
    }
}

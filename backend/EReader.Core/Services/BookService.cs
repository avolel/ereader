using System.Globalization;
using System.Text;
using System.Text.Json;
using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;

namespace EReader.Core.Services;

public sealed class BookService : IBookService
{
    private readonly IBookRepository _books;
    private readonly IBookFileStore _files;
    private readonly IEpubAssetReader _assets;

    public BookService(
        IBookRepository books,
        IBookFileStore files,
        IEpubAssetReader assets)
    {
        _books = books;
        _files = files;
        _assets = assets;
    }

    public async Task<BookListPage> ListAsync(
        Guid userId,
        BookSortKey sortKey,
        SortDirection sortDir,
        BookListFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var decoded = DecodeCursor(cursor);
        if (decoded is not null && (decoded.SortKey != sortKey || decoded.SortDir != sortDir))
        {
            // The cursor was minted against a different sort. Silently re-running with
            // the requested sort would return overlapping/missing rows — fail loudly so
            // the client either keeps the original sort or starts fresh without a cursor.
            throw new ValidationException("Cursor was issued for a different sort; omit the cursor when changing sort.");
        }

        var (items, hasMore) = await _books.ListAsync(userId, sortKey, sortDir, filter, decoded, pageSize, ct);

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = EncodeCursor(new BookListCursor(
                sortKey,
                sortDir,
                ExtractSortValue(last, sortKey),
                last.Id));
        }
        return new BookListPage(items, nextCursor);
    }

    public async Task<BookWithChapters> GetByIdAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _books.GetByIdWithChaptersAsync(bookId, userId, ct)
            ?? throw new NotFoundException("Book not found.");
        var chapters = book.Chapters.OrderBy(c => c.SpineOrder).ToList();
        return new BookWithChapters(book, chapters);
    }

    public async Task<ChapterContent> GetChapterAsync(
        Guid bookId,
        Guid chapterId,
        Guid userId,
        string assetBaseUrl,
        CancellationToken ct)
    {
        var chapter = await _books.GetChapterAsync(bookId, chapterId, userId, ct)
            ?? throw new NotFoundException("Chapter not found.");

        var spineIds = await _books.GetChapterIdsInSpineOrderAsync(bookId, userId, ct);
        int index = -1;
        for (int i = 0; i < spineIds.Count; i++)
        {
            if (spineIds[i] == chapter.Id) { index = i; break; }
        }
        Guid? previousId = index > 0 ? spineIds[index - 1] : null;
        Guid? nextId = index >= 0 && index < spineIds.Count - 1 ? spineIds[index + 1] : null;

        var rewritten = AssetUrlRewriter.Rewrite(
            chapter.ContentText ?? string.Empty,
            chapter.ContentHref,
            assetBaseUrl);

        return new ChapterContent(chapter, rewritten, previousId, nextId);
    }

    public async Task<BookAsset> GetAssetAsync(Guid bookId, Guid userId, string assetPath, CancellationToken ct)
    {
        var book = await _books.GetByIdAsync(bookId, userId, ct)
            ?? throw new NotFoundException("Book not found.");

        if (string.IsNullOrWhiteSpace(book.FilePath) || !await _files.ExistsAsync(book.FilePath, ct))
            throw new NotFoundException("Book source file is missing.");

        var source = await _files.OpenReadAsync(book.FilePath, ct);
        var asset = _assets.OpenAsset(source, assetPath);   // takes ownership of `source`
        if (asset is null) { source.Dispose(); throw new NotFoundException("Asset not found."); }
        return asset;
    }

    public async Task<BookAsset> GetCoverAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _books.GetByIdAsync(bookId, userId, ct)
            ?? throw new NotFoundException("Book not found.");

        if (string.IsNullOrWhiteSpace(book.CoverImagePath) || !await _files.ExistsAsync(book.CoverImagePath, ct))
            throw new NotFoundException("Cover not found.");

        var stream = await _files.OpenReadAsync(book.CoverImagePath, ct);
        var contentType = ContentTypeFromExtension(Path.GetExtension(book.CoverImagePath));
        var fileName = Path.GetFileName(book.CoverImagePath); // still works on an object key
        return new BookAsset(stream, contentType, stream.Length, fileName);
    }

    public async Task DeleteAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _books.GetByIdAsync(bookId, userId, ct)
            ?? throw new NotFoundException("Book not found.");
        await _books.RemoveAsync(book, ct);
        await _files.DeleteForBookAsync(bookId, ct); // was: _files.DeleteForBook(bookId)
    }

    private static string ExtractSortValue(Models.Book b, BookSortKey key) => key switch
    {
        BookSortKey.Title => b.Title,
        BookSortKey.Author => b.Author,
        BookSortKey.ImportedAt => b.ImportedAt.Ticks.ToString(CultureInfo.InvariantCulture),
        _ => throw new ValidationException("Unknown sort key."),
    };

    // Cursor: base64url(JSON({k,d,v,id})). JSON avoids delimiter collisions when
    // sortValue is a title containing colons; size overhead is negligible.
    internal static string EncodeCursor(BookListCursor cursor)
    {
        var payload = new CursorPayload(
            (int)cursor.SortKey,
            (int)cursor.SortDir,
            cursor.SortValue,
            cursor.BookId);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(json);
    }

    internal static BookListCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        // Surface malformed cursors as a 400 rather than silently restarting from page 1.
        // A client paginating forward with a corrupt cursor would otherwise loop on page 1
        // forever and never see the bug; better to fail loudly.
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var payload = JsonSerializer.Deserialize<CursorPayload>(bytes)
                ?? throw new ValidationException("Invalid cursor.");
            if (!Enum.IsDefined(typeof(BookSortKey), payload.K)
                || !Enum.IsDefined(typeof(SortDirection), payload.D))
            {
                throw new ValidationException("Invalid cursor.");
            }
            return new BookListCursor(
                (BookSortKey)payload.K,
                (SortDirection)payload.D,
                payload.V ?? string.Empty,
                payload.Id);
        }
        catch (FormatException)
        {
            throw new ValidationException("Invalid cursor.");
        }
        catch (JsonException)
        {
            throw new ValidationException("Invalid cursor.");
        }
    }

    // Field names kept terse to keep the encoded cursor short.
    private sealed record CursorPayload(int K, int D, string? V, Guid Id);

    private static string ContentTypeFromExtension(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
    }
}

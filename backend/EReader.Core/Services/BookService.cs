using System.Globalization;
using System.Text;
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

    public async Task<BookListPage> ListAsync(Guid userId, string? cursor, int pageSize, CancellationToken ct)
    {
        var (cursorImportedAt, cursorBookId) = DecodeCursor(cursor);
        var (items, hasMore) = await _books.ListAsync(userId, cursorImportedAt, cursorBookId, pageSize, ct);

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = EncodeCursor(last.ImportedAt, last.Id);
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

        if (string.IsNullOrWhiteSpace(book.FilePath) || !_files.Exists(book.FilePath))
        {
            throw new NotFoundException("Book source file is missing.");
        }

        var asset = _assets.OpenAsset(book.FilePath, assetPath)
            ?? throw new NotFoundException("Asset not found.");
        return asset;
    }

    public async Task<BookAsset> GetCoverAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _books.GetByIdAsync(bookId, userId, ct)
            ?? throw new NotFoundException("Book not found.");

        if (string.IsNullOrWhiteSpace(book.CoverImagePath) || !_files.Exists(book.CoverImagePath))
        {
            throw new NotFoundException("Cover not found.");
        }

        var stream = _files.OpenRead(book.CoverImagePath);
        var contentType = ContentTypeFromExtension(Path.GetExtension(book.CoverImagePath));
        var fileName = Path.GetFileName(book.CoverImagePath);
        return new BookAsset(stream, contentType, stream.Length, fileName);
    }

    public async Task DeleteAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _books.GetByIdAsync(bookId, userId, ct)
            // 404 (not 403) when the book exists for a different user — the plan
            // calls this out: standard "don't leak existence" pattern.
            ?? throw new NotFoundException("Book not found.");

        await _books.RemoveAsync(book, ct);
        _files.DeleteForBook(bookId);
    }

    // Cursor format: base64url("{ImportedAt-ticks}:{book-guid}").
    // Opaque to clients; encoding keeps them from interpreting/manipulating it.
    internal static string EncodeCursor(DateTime importedAt, Guid bookId)
    {
        var payload = $"{importedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{bookId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    internal static (DateTime? ImportedAt, Guid? BookId) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);

        // Surface malformed cursors as a 400 rather than silently restarting from page 1.
        // A client paginating forward with a corrupt cursor would otherwise loop on page 1
        // forever and never see the bug; better to fail loudly.
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2) throw new ValidationException("Invalid cursor.");
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                throw new ValidationException("Invalid cursor.");
            }
            if (!Guid.TryParse(parts[1], out var id)) throw new ValidationException("Invalid cursor.");
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
            throw new ValidationException("Invalid cursor.");
        }
    }

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

using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface IBookService
{
    Task<BookListPage> ListAsync(Guid userId, string? cursor, int pageSize, CancellationToken ct);

    Task<BookWithChapters> GetByIdAsync(Guid bookId, Guid userId, CancellationToken ct);

    // assetBaseUrl is the absolute prefix to use when rewriting relative asset hrefs
    // inside chapter content — e.g. "/api/v1/books/{bookId}/assets". The trailing
    // segment of the rewritten URL is the in-EPUB asset path.
    Task<ChapterContent> GetChapterAsync(
        Guid bookId,
        Guid chapterId,
        Guid userId,
        string assetBaseUrl,
        CancellationToken ct);

    Task<BookAsset> GetAssetAsync(Guid bookId, Guid userId, string assetPath, CancellationToken ct);

    Task<BookAsset> GetCoverAsync(Guid bookId, Guid userId, CancellationToken ct);

    Task DeleteAsync(Guid bookId, Guid userId, CancellationToken ct);
}

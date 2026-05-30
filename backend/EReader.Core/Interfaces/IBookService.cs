using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface IBookService
{
    // sortKey/sortDir/filter are taken from the request; cursor is opaque and encodes
    // the keyset position only. If a cursor is supplied with a different sort than was
    // used to mint it, the service rejects the request — see BookService for details.
    Task<BookListPage> ListAsync(
        Guid userId,
        BookSortKey sortKey,
        SortDirection sortDir,
        BookListFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken ct);

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

using EReader.Core.Books;
using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(Guid bookId, Guid userId, CancellationToken ct);

    Task<Book?> GetByIdWithChaptersAsync(Guid bookId, Guid userId, CancellationToken ct);

    Task<Chapter?> GetChapterAsync(Guid bookId, Guid chapterId, Guid userId, CancellationToken ct);

    // Spine-ordered Ids only. Used to resolve prev/next siblings without
    // hydrating the full chapter rows.
    Task<IReadOnlyList<Guid>> GetChapterIdsInSpineOrderAsync(Guid bookId, Guid userId, CancellationToken ct);

    Task<bool> ExistsByHashAsync(Guid userId, string fileHash, CancellationToken ct);

    // Keyset pagination on (sortKey, Id). Cursor is decoded into BookListCursor
    // by the service before being passed in. Filter is independent of cursor — the
    // service trusts the client to keep filter values stable across page requests.
    Task<(IReadOnlyList<Book> Items, bool HasMore)> ListAsync(
        Guid userId,
        BookSortKey sortKey,
        SortDirection sortDir,
        BookListFilter filter,
        BookListCursor? cursor,
        int pageSize,
        CancellationToken ct);

    Task AddAsync(Book book, IEnumerable<Chapter> chapters, CancellationToken ct);

    Task RemoveAsync(Book book, CancellationToken ct);
}

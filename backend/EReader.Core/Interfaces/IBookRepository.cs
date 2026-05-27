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

    // Cursor is opaque to the caller (encoded ImportedAt + Id). pageSize+1 is fetched
    // internally; the repo returns at most pageSize items and a flag indicating more.
    Task<(IReadOnlyList<Book> Items, bool HasMore)> ListAsync(
        Guid userId,
        DateTime? cursorImportedAt,
        Guid? cursorBookId,
        int pageSize,
        CancellationToken ct);

    Task AddAsync(Book book, IEnumerable<Chapter> chapters, CancellationToken ct);

    Task RemoveAsync(Book book, CancellationToken ct);
}

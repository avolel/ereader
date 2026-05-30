using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface ISearchRepository
{
    // Returns at most `pageSize` items plus a flag indicating whether at least one
    // more row exists past the page boundary. Ordering is (BookId, SpineOrder) so
    // keyset pagination is stable. userId is enforced at the SQL level — callers
    // can't accidentally widen the scope.
    Task<(IReadOnlyList<SearchHit> Items, bool HasMore)> SearchAsync(
        Guid userId,
        string query,
        Guid? bookFilter,
        Guid? cursorBookId,
        int? cursorSpineOrder,
        int pageSize,
        CancellationToken ct);
}

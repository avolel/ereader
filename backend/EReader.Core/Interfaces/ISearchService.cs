using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface ISearchService
{
    Task<SearchPage> SearchAsync(
        Guid userId,
        string query,
        Guid? bookFilter,
        string? cursor,
        int pageSize,
        CancellationToken ct);
}

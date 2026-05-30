using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IReadingSettingsRepository
{
    // bookId == null fetches the global default row for the user.
    Task<ReadingSetting?> GetAsync(Guid userId, Guid? bookId, CancellationToken ct);

    Task AddAsync(ReadingSetting setting, CancellationToken ct);

    Task UpdateAsync(ReadingSetting setting, CancellationToken ct);

    Task RemoveAsync(ReadingSetting setting, CancellationToken ct);

    Task<bool> BookExistsForUserAsync(Guid bookId, Guid userId, CancellationToken ct);
}
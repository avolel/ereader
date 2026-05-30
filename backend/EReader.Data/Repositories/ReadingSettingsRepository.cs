using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data.Repositories;

public sealed class ReadingSettingsRepository : IReadingSettingsRepository
{
    private readonly EReaderDbContext _db;

    public ReadingSettingsRepository(EReaderDbContext db)
    {
        _db = db;
    }

    public Task<ReadingSetting?> GetAsync(Guid userId, Guid? bookId, CancellationToken ct) =>
        _db.ReadingSettings
            .FirstOrDefaultAsync(
                rs => rs.UserId == userId && rs.BookId == bookId,
                ct);

    public async Task AddAsync(ReadingSetting setting, CancellationToken ct)
    {
        _db.ReadingSettings.Add(setting);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ReadingSetting setting, CancellationToken ct)
    {
        _db.ReadingSettings.Update(setting);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(ReadingSetting setting, CancellationToken ct)
    {
        _db.ReadingSettings.Remove(setting);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> BookExistsForUserAsync(Guid bookId, Guid userId, CancellationToken ct) =>
        _db.Books.AnyAsync(b => b.Id == bookId && b.UserId == userId, ct);
}
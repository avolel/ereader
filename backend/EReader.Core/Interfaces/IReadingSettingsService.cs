using EReader.Core.Models;
using EReader.Core.ReadingSettings;

namespace EReader.Core.Interfaces;

public interface IReadingSettingsService
{
    // GET /me — global defaults. Returns the user's saved row if it exists,
    // otherwise a transient default-valued ReadingSetting (not persisted) so
    // the client always has something to render.
    Task<ReadingSetting> GetGlobalAsync(Guid userId, CancellationToken ct);

    // GET /books/{bookId} — per-book override merged on top of global defaults.
    // If no per-book row exists, returns the global row (or transient defaults).
    Task<ReadingSetting> GetForBookAsync(Guid bookId, Guid userId, CancellationToken ct);

    // PUT /me — upsert global defaults. Null fields in the update are left as-is.
    Task<ReadingSetting> UpsertGlobalAsync(Guid userId, TypographyUpdate update, CancellationToken ct);

    // PUT /books/{bookId} — upsert per-book typography override.
    Task<ReadingSetting> UpsertForBookAsync(
        Guid bookId,
        Guid userId,
        TypographyUpdate update,
        CancellationToken ct);

    // DELETE /books/{bookId} — drop the per-book override; future reads fall
    // back to the global default. No-op if no override exists.
    Task DeleteForBookAsync(Guid bookId, Guid userId, CancellationToken ct);

    // PUT /books/{bookId}/position — separate endpoint because position updates
    // are high-frequency and don't share validation with typography.
    Task<ReadingSetting> UpdatePositionAsync(
        Guid bookId,
        Guid userId,
        PositionUpdate update,
        CancellationToken ct);
}
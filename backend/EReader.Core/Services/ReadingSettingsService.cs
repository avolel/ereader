using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.ReadingSettings;

namespace EReader.Core.Services;

public sealed class ReadingSettingsService : IReadingSettingsService
{
    // Allow-lists keep accidental typos from the client (or future renames) from
    // silently persisting garbage. Values match what the frontend ships.
    private static readonly HashSet<string> AllowedThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "light", "dark", "system",
    };
    private static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "system",
    };

    private const int MinFontSize = 12;
    private const int MaxFontSize = 28;
    private const decimal MinLineSpacing = 1.0m;
    private const decimal MaxLineSpacing = 2.4m;
    private const int MinMargin = 0;
    private const int MaxMargin = 200;

    private readonly IReadingSettingsRepository _repo;

    public ReadingSettingsService(IReadingSettingsRepository repo)
    {
        _repo = repo;
    }

    public async Task<ReadingSetting> GetGlobalAsync(Guid userId, CancellationToken ct)
    {
        var existing = await _repo.GetAsync(userId, null, ct);
        return existing ?? BuildTransientDefault(userId, bookId: null);
    }

    public async Task<ReadingSetting> GetForBookAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        var perBook = await _repo.GetAsync(userId, bookId, ct);
        if (perBook is not null) return perBook;

        // No per-book override; fall back to the global row (or transient default).
        var global = await _repo.GetAsync(userId, null, ct);
        return global ?? BuildTransientDefault(userId, bookId: null);
    }

    public Task<ReadingSetting> UpsertGlobalAsync(
        Guid userId,
        TypographyUpdate update,
        CancellationToken ct) =>
        UpsertTypographyAsync(userId, bookId: null, update, ct);

    public async Task<ReadingSetting> UpsertForBookAsync(
        Guid bookId,
        Guid userId,
        TypographyUpdate update,
        CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        return await UpsertTypographyAsync(userId, bookId, update, ct);
    }

    public async Task DeleteForBookAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        var existing = await _repo.GetAsync(userId, bookId, ct);
        if (existing is null) return; // Idempotent: deleting a missing override is fine.
        await _repo.RemoveAsync(existing, ct);
    }

    public async Task<ReadingSetting> UpdatePositionAsync(
        Guid bookId,
        Guid userId,
        PositionUpdate update,
        CancellationToken ct)
    {
        if (update.ScrollOffset < 0)
        {
            throw new ValidationException("scrollOffset must be >= 0.");
        }
        await EnsureBookOwnedAsync(bookId, userId, ct);

        var now = DateTime.UtcNow;
        var existing = await _repo.GetAsync(userId, bookId, ct);
        if (existing is null)
        {
            // No per-book row yet — seed one carrying just position. Typography
            // fields use defaults; a subsequent typography upsert will set them.
            var seeded = BuildTransientDefault(userId, bookId);
            seeded.LastChapterId = update.ChapterId;
            seeded.LastScrollOffset = update.ScrollOffset;
            seeded.LastReadAt = now;
            seeded.UpdatedAt = now;
            await _repo.AddAsync(seeded, ct);
            return seeded;
        }

        existing.LastChapterId = update.ChapterId;
        existing.LastScrollOffset = update.ScrollOffset;
        existing.LastReadAt = now;
        existing.UpdatedAt = now;
        await _repo.UpdateAsync(existing, ct);
        return existing;
    }

    private async Task<ReadingSetting> UpsertTypographyAsync(
        Guid userId,
        Guid? bookId,
        TypographyUpdate update,
        CancellationToken ct)
    {
        ValidateTypography(update);
        var now = DateTime.UtcNow;
        var existing = await _repo.GetAsync(userId, bookId, ct);
        if (existing is null)
        {
            var seeded = BuildTransientDefault(userId, bookId);
            ApplyTypography(seeded, update);
            seeded.UpdatedAt = now;
            await _repo.AddAsync(seeded, ct);
            return seeded;
        }

        ApplyTypography(existing, update);
        existing.UpdatedAt = now;
        await _repo.UpdateAsync(existing, ct);
        return existing;
    }

    private async Task EnsureBookOwnedAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        // 404 (not 403) when the book belongs to a different user — same
        // "don't leak existence" pattern as BookService.
        var owned = await _repo.BookExistsForUserAsync(bookId, userId, ct);
        if (!owned) throw new NotFoundException("Book not found.");
    }

    private static void ValidateTypography(TypographyUpdate update)
    {
        if (update.Theme is { } theme && !AllowedThemes.Contains(theme))
        {
            throw new ValidationException("theme must be one of: light, dark, system.");
        }
        if (update.FontFamily is { } font && !AllowedFonts.Contains(font))
        {
            throw new ValidationException("fontFamily must be one of: serif, sans-serif, monospace, system.");
        }
        if (update.FontSize is { } size && (size < MinFontSize || size > MaxFontSize))
        {
            throw new ValidationException($"fontSize must be between {MinFontSize} and {MaxFontSize}.");
        }
        if (update.LineSpacing is { } spacing && (spacing < MinLineSpacing || spacing > MaxLineSpacing))
        {
            throw new ValidationException($"lineSpacing must be between {MinLineSpacing} and {MaxLineSpacing}.");
        }
        if (update.MarginHorizontal is { } mh && (mh < MinMargin || mh > MaxMargin))
        {
            throw new ValidationException($"marginHorizontal must be between {MinMargin} and {MaxMargin}.");
        }
        if (update.MarginVertical is { } mv && (mv < MinMargin || mv > MaxMargin))
        {
            throw new ValidationException($"marginVertical must be between {MinMargin} and {MaxMargin}.");
        }
    }

    private static void ApplyTypography(ReadingSetting target, TypographyUpdate update)
    {
        if (update.Theme is { } theme) target.Theme = theme.ToLowerInvariant();
        if (update.FontFamily is { } font) target.FontFamily = font.ToLowerInvariant();
        if (update.FontSize is { } size) target.FontSize = size;
        if (update.LineSpacing is { } spacing) target.LineSpacing = spacing;
        if (update.MarginHorizontal is { } mh) target.MarginHorizontal = mh;
        if (update.MarginVertical is { } mv) target.MarginVertical = mv;
    }

    private static ReadingSetting BuildTransientDefault(Guid userId, Guid? bookId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        BookId = bookId,
        // Model already sets sensible defaults; this is just the assembly point.
    };
}
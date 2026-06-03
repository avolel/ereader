using System.Text.Json;
using EReader.Core.Annotations;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;

namespace EReader.Core.Services;

public class AnnotationService : IAnnotationService
{
    private const int MaxNoteLength = 10_000;
    private const int MaxLabelLength = 200;
    private const int MaxAnchorLength = 8_000;
    private const int MaxSelectedTextLength = 4_000;

    // Highlight colours are an allow-list, same defensive stance as ReadingSettings
    // themes/fonts — keeps typos/garbage out of the column. Values match the frontend.
    private static readonly HashSet<string> AllowedColours = new(StringComparer.OrdinalIgnoreCase)
    {
        "yellow", "green", "blue", "pink", "orange",
    };

    private readonly IAnnotationRepository _repo;

    public AnnotationService(IAnnotationRepository repo)
    {
        _repo = repo;
    }

    public async Task<Annotation> CreateAnnotationAsync(Guid bookId, Guid userId, CreateAnnotationInput input, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        ValidateAnchor(input.TextAnchor, input.SelectedText);
        await ValidateChapterAsync(input.ChapterId, bookId, ct);

        if (input.Type == AnnotationType.Highlight)
        {
            ValidateColour(input.Colour); // highlights require a valid colour
        }
        if (input.Type == AnnotationType.Note && string.IsNullOrWhiteSpace(input.NoteBody))
        {
            throw new ValidationException("A note requires a non-empty noteBody.");
        }
        ValidateNoteLength(input.NoteBody);

        var now = DateTime.UtcNow;
        var annotation = new Annotation
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            UserId = userId,
            ChapterId = input.ChapterId,
            Type = input.Type,
            Colour = input.Type == AnnotationType.Highlight ? input.Colour!.ToLowerInvariant() : input.Colour?.ToLowerInvariant(),
            TextAnchor = input.TextAnchor,
            SelectedText = input.SelectedText,
            NoteBody = input.NoteBody,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _repo.AddAnnotationAsync(annotation, ct);
        return annotation;
    }

    public async Task<Bookmark> CreateBookmarkAsync(Guid bookId, Guid userId, CreateBookmarkInput input, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        ValidateAnchor(input.TextAnchor, selectedText: null);
        await ValidateChapterAsync(input.ChapterId, bookId, ct);
        ValidateLabel(input.Label);

        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            UserId = userId,
            ChapterId = input.ChapterId,
            TextAnchor = input.TextAnchor,
            Label = input.Label,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddBookmarkAsync(bookmark, ct);
        return bookmark;
    }

    public async Task DeleteAnnotationAsync(Guid bookId, Guid annotationId, Guid userId, CancellationToken ct)
    {
        var existing = await _repo.GetAnnotationAsync(annotationId, userId, ct);
        if (existing is null || existing.BookId != bookId)
        {
            throw new NotFoundException("Annotation not found.");
        }
        await _repo.RemoveAnnotationAsync(existing, ct);
    }

    public async Task DeleteBookmarkAsync(Guid bookId, Guid bookmarkId, Guid userId, CancellationToken ct)
    {
        var existing = await _repo.GetBookmarkAsync(bookmarkId, userId, ct);
        if (existing is null || existing.BookId != bookId)
        {
            throw new NotFoundException("Bookmark not found.");
        }
        await _repo.RemoveBookmarkAsync(existing, ct);
    }

    public async Task<AnnotationPage> ListAnnotationsAsync(Guid bookId, Guid userId, string? cursor, int pageSize, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        var decoded = DecodeCursor(cursor);
        var (items, hasMore) = await _repo.ListAnnotationsAsync(bookId, userId, decoded, pageSize, ct);
        var next = (hasMore && items.Count > 0)
            ? EncodeCursor(new AnnotationCursor(items[^1].CreatedAt, items[^1].Id))
            : null;
        return new AnnotationPage(items, next);
    }

    public async Task<BookmarkPage> ListBookmarksAsync(Guid bookId, Guid userId, string? cursor, int pageSize, CancellationToken ct)
    {
        await EnsureBookOwnedAsync(bookId, userId, ct);
        var decoded = DecodeCursor(cursor);
        var (items, hasMore) = await _repo.ListBookmarksAsync(bookId, userId, decoded, pageSize, ct);
        var next = (hasMore && items.Count > 0)
            ? EncodeCursor(new AnnotationCursor(items[^1].CreatedAt, items[^1].Id))
            : null;
        return new BookmarkPage(items, next);
    }

    public async Task<Annotation> UpdateAnnotationAsync(Guid bookId, Guid annotationId, Guid userId, UpdateAnnotationInput input, CancellationToken ct)
    {
         var existing = await _repo.GetAnnotationAsync(annotationId, userId, ct);
        // 404 if missing OR it belongs to another user OR to a different book — never
        // leak existence (same rule as BookService).
        if (existing is null || existing.BookId != bookId)
        {
            throw new NotFoundException("Annotation not found.");
        }

        if (input.Colour is not null)
        {
            ValidateColour(input.Colour);
            existing.Colour = input.Colour.ToLowerInvariant();
        }
        if (input.NoteBody is not null)
        {
            ValidateNoteLength(input.NoteBody);
            existing.NoteBody = input.NoteBody;
        }
        existing.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAnnotationAsync(existing, ct);
        return existing;
    }

    public async Task<Bookmark> UpdateBookmarkAsync(Guid bookId, Guid bookmarkId, Guid userId, UpdateBookmarkInput input, CancellationToken ct)
    {
        var existing = await _repo.GetBookmarkAsync(bookmarkId, userId, ct);
        if (existing is null || existing.BookId != bookId)
        {
            throw new NotFoundException("Bookmark not found.");
        }
        if (input.Label is not null)
        {
            ValidateLabel(input.Label);
            existing.Label = input.Label;
        }
        await _repo.UpdateBookmarkAsync(existing, ct);
        return existing;
    }

    #region ---------- Shared helpers ----------
        private async Task EnsureBookOwnedAsync(Guid bookId, Guid userId, CancellationToken ct)
        {
            if (!await _repo.BookExistsForUserAsync(bookId, userId, ct))
            {
                throw new NotFoundException("Book not found.");
            }
        }

        private async Task ValidateChapterAsync(Guid? chapterId, Guid bookId, CancellationToken ct)
        {
            if (chapterId is { } cid && !await _repo.ChapterBelongsToBookAsync(cid, bookId, ct))
            {
                throw new ValidationException("chapterId does not belong to this book.");
            }
        }

        // The anchor is opaque, but we still sanity-check it parses as our selector shape
        // and is within size bounds — catches obviously broken clients without coupling the
        // backend to the selector semantics.
        private static void ValidateAnchor(string anchor, string? selectedText)
        {
            if (string.IsNullOrWhiteSpace(anchor) || anchor.Length > MaxAnchorLength)
            {
                throw new ValidationException("textAnchor is required and must be reasonable in size.");
            }
            try
            {
                using var doc = JsonDocument.Parse(anchor);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("start", out _)
                    || !doc.RootElement.TryGetProperty("end", out _))
                {
                    throw new ValidationException("textAnchor must be a selector object with start/end.");
                }
            }
            catch (JsonException)
            {
                throw new ValidationException("textAnchor must be valid JSON.");
            }
            if (selectedText is { Length: > MaxSelectedTextLength })
            {
                throw new ValidationException($"selectedText must be <= {MaxSelectedTextLength} characters.");
            }
        }

        private static void ValidateColour(string? colour)
        {
            if (string.IsNullOrWhiteSpace(colour) || !AllowedColours.Contains(colour))
            {
                throw new ValidationException("colour must be one of: yellow, green, blue, pink, orange.");
            }
        }

        private static void ValidateNoteLength(string? note)
        {
            if (note is { Length: > MaxNoteLength })
            {
                throw new ValidationException($"noteBody must be <= {MaxNoteLength} characters.");
            }
        }

        private static void ValidateLabel(string? label)
        {
            if (label is { Length: > MaxLabelLength })
            {
                throw new ValidationException($"label must be <= {MaxLabelLength} characters.");
            }
        }

        // Cursor: base64(JSON({t,id})) where t = CreatedAt ticks. Single fixed sort
        // (CreatedAt desc, Id desc) so — unlike BookService — no sort key is embedded.
        internal static string EncodeCursor(AnnotationCursor cursor)
        {
            var payload = new CursorPayload(cursor.CreatedAt.Ticks, cursor.Id);
            return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));
        }

        internal static AnnotationCursor? DecodeCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor)) return null;
            try
            {
                var bytes = Convert.FromBase64String(cursor);
                var payload = JsonSerializer.Deserialize<CursorPayload>(bytes)
                    ?? throw new ValidationException("Invalid cursor.");
                return new AnnotationCursor(new DateTime(payload.T, DateTimeKind.Utc), payload.Id);
            }
            catch (FormatException) { throw new ValidationException("Invalid cursor."); }
            catch (JsonException) { throw new ValidationException("Invalid cursor."); }
        }

        private sealed record CursorPayload(long T, Guid Id);
    #endregion
}

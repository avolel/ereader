using EReader.Core.Annotations;
using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IAnnotationRepository
{
    // Reused ownership gate — identical to the reading-settings repo so the 404-not-403
    // rule lives in one place per repo.
    Task<bool> BookExistsForUserAsync(Guid bookId, Guid userId, CancellationToken ct);

    // ---- Annotations ----
    Task<Annotation?> GetAnnotationAsync(Guid id, Guid userId, CancellationToken ct);
    Task<(IReadOnlyList<Annotation> Items, bool HasMore)> ListAnnotationsAsync(
        Guid bookId, Guid userId, AnnotationCursor? cursor, int pageSize, CancellationToken ct);
    Task AddAnnotationAsync(Annotation annotation, CancellationToken ct);
    Task UpdateAnnotationAsync(Annotation annotation, CancellationToken ct);
    Task RemoveAnnotationAsync(Annotation annotation, CancellationToken ct);

    // ---- Bookmarks ----
    Task<Bookmark?> GetBookmarkAsync(Guid id, Guid userId, CancellationToken ct);
    Task<(IReadOnlyList<Bookmark> Items, bool HasMore)> ListBookmarksAsync(
        Guid bookId, Guid userId, AnnotationCursor? cursor, int pageSize, CancellationToken ct);
    Task AddBookmarkAsync(Bookmark bookmark, CancellationToken ct);
    Task UpdateBookmarkAsync(Bookmark bookmark, CancellationToken ct);
    Task RemoveBookmarkAsync(Bookmark bookmark, CancellationToken ct);

    // Validate a chapterId belongs to the book (so we don't persist a dangling anchor).
    Task<bool> ChapterBelongsToBookAsync(Guid chapterId, Guid bookId, CancellationToken ct);
}

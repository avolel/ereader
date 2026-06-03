using EReader.Core.Annotations;
using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IAnnotationService
{
    // ---- Annotations (highlights + notes) ----
    Task<AnnotationPage> ListAnnotationsAsync(
        Guid bookId, Guid userId, string? cursor, int pageSize, CancellationToken ct);
    Task<Annotation> CreateAnnotationAsync(
        Guid bookId, Guid userId, CreateAnnotationInput input, CancellationToken ct);
    Task<Annotation> UpdateAnnotationAsync(
        Guid bookId, Guid annotationId, Guid userId, UpdateAnnotationInput input, CancellationToken ct);
    Task DeleteAnnotationAsync(Guid bookId, Guid annotationId, Guid userId, CancellationToken ct);

    // ---- Bookmarks ----
    Task<BookmarkPage> ListBookmarksAsync(
        Guid bookId, Guid userId, string? cursor, int pageSize, CancellationToken ct);
    Task<Bookmark> CreateBookmarkAsync(
        Guid bookId, Guid userId, CreateBookmarkInput input, CancellationToken ct);
    Task<Bookmark> UpdateBookmarkAsync(
        Guid bookId, Guid bookmarkId, Guid userId, UpdateBookmarkInput input, CancellationToken ct);
    Task DeleteBookmarkAsync(Guid bookId, Guid bookmarkId, Guid userId, CancellationToken ct);
}

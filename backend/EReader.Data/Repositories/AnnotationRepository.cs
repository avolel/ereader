using EReader.Core.Annotations;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data.Repositories;

public sealed class AnnotationRepository: IAnnotationRepository
{
    private readonly EReaderDbContext _db;

    public AnnotationRepository(EReaderDbContext db)
    {
        _db = db;
    }

    public Task<bool> BookExistsForUserAsync(Guid bookId, Guid userId, CancellationToken ct) =>
        _db.Books.AnyAsync(b => b.Id == bookId && b.UserId == userId, ct);

    public Task<bool> ChapterBelongsToBookAsync(Guid chapterId, Guid bookId, CancellationToken ct) =>
        _db.Chapters.AnyAsync(c => c.Id == chapterId && c.BookId == bookId, ct);

    // ---- Annotations ----

    public Task<Annotation?> GetAnnotationAsync(Guid id, Guid userId, CancellationToken ct) =>
        _db.Annotations.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

    public async Task<(IReadOnlyList<Annotation> Items, bool HasMore)> ListAnnotationsAsync(
        Guid bookId, Guid userId, AnnotationCursor? cursor, int pageSize, CancellationToken ct)
    {
        var query = _db.Annotations.Where(a => a.BookId == bookId && a.UserId == userId);
        if (cursor is not null)
        {
            var ts = cursor.CreatedAt;
            var id = cursor.Id;
            query = query.Where(a => a.CreatedAt < ts || (a.CreatedAt == ts && a.Id.CompareTo(id) < 0));
        }
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return (rows, hasMore);
    }

    public async Task AddAnnotationAsync(Annotation annotation, CancellationToken ct)
    {
        _db.Annotations.Add(annotation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAnnotationAsync(Annotation annotation, CancellationToken ct)
    {
        _db.Annotations.Update(annotation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAnnotationAsync(Annotation annotation, CancellationToken ct)
    {
        _db.Annotations.Remove(annotation);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Bookmarks ----  (same shape as annotations)

    public Task<Bookmark?> GetBookmarkAsync(Guid id, Guid userId, CancellationToken ct) =>
        _db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);

    public async Task<(IReadOnlyList<Bookmark> Items, bool HasMore)> ListBookmarksAsync(
        Guid bookId, Guid userId, AnnotationCursor? cursor, int pageSize, CancellationToken ct)
    {
        var query = _db.Bookmarks.Where(b => b.BookId == bookId && b.UserId == userId);
        if (cursor is not null)
        {
            var ts = cursor.CreatedAt;
            var id = cursor.Id;
            query = query.Where(b => b.CreatedAt < ts || (b.CreatedAt == ts && b.Id.CompareTo(id) < 0));
        }
        var rows = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return (rows, hasMore);
    }

    public async Task AddBookmarkAsync(Bookmark bookmark, CancellationToken ct)
    {
        _db.Bookmarks.Add(bookmark);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateBookmarkAsync(Bookmark bookmark, CancellationToken ct)
    {
        _db.Bookmarks.Update(bookmark);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveBookmarkAsync(Bookmark bookmark, CancellationToken ct)
    {
        _db.Bookmarks.Remove(bookmark);
        await _db.SaveChangesAsync(ct);
    }
}

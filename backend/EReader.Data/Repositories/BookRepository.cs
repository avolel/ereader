using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data.Repositories;

public sealed class BookRepository : IBookRepository
{
    private readonly EReaderDbContext _db;

    public BookRepository(EReaderDbContext db)
    {
        _db = db;
    }

    public Task<Book?> GetByIdAsync(Guid bookId, Guid userId, CancellationToken ct) =>
        _db.Books.FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct);

    public async Task<Book?> GetByIdWithChaptersAsync(Guid bookId, Guid userId, CancellationToken ct)
    {
        var book = await _db.Books
            .Include(b => b.Chapters.OrderBy(c => c.SpineOrder))
            .FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct);
        return book;
    }

    public Task<Chapter?> GetChapterAsync(Guid bookId, Guid chapterId, Guid userId, CancellationToken ct) =>
        _db.Chapters
            .Where(c => c.Id == chapterId && c.BookId == bookId && c.Book.UserId == userId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetChapterIdsInSpineOrderAsync(
        Guid bookId,
        Guid userId,
        CancellationToken ct)
    {
        var ids = await _db.Chapters
            .Where(c => c.BookId == bookId && c.Book.UserId == userId)
            .OrderBy(c => c.SpineOrder)
            .Select(c => c.Id)
            .ToListAsync(ct);
        return ids;
    }

    public Task<bool> ExistsByHashAsync(Guid userId, string fileHash, CancellationToken ct) =>
        _db.Books.AnyAsync(b => b.UserId == userId && b.FileHash == fileHash, ct);

    public async Task<(IReadOnlyList<Book> Items, bool HasMore)> ListAsync(
        Guid userId,
        DateTime? cursorImportedAt,
        Guid? cursorBookId,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.Books.Where(b => b.UserId == userId);

        if (cursorImportedAt is { } ts)
        {
            // Cursor points at the last item of the previous page. ImportedAt is set
            // from DateTime.UtcNow (~100ns precision), so ties between two ingestions
            // are effectively impossible — a strict less-than is sufficient and stays
            // trivially translatable to SQL.
            query = query.Where(b => b.ImportedAt < ts);
            if (cursorBookId is { } id)
            {
                // Belt-and-braces: if a tie ever did happen, exclude the cursor row
                // explicitly so it can't repeat. This stays translatable (simple !=).
                query = query.Where(b => b.Id != id);
            }
        }

        // Fetch pageSize+1 to know if there's a next page without a separate count.
        var rows = await query
            .OrderByDescending(b => b.ImportedAt)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return (rows, hasMore);
    }

    public async Task AddAsync(Book book, IEnumerable<Chapter> chapters, CancellationToken ct)
    {
        // SaveChangesAsync is already atomic — EF opens an implicit transaction
        // for a single call, so no explicit BeginTransaction is needed.
        await _db.Books.AddAsync(book, ct);
        await _db.Chapters.AddRangeAsync(chapters, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Book book, CancellationToken ct)
    {
        _db.Books.Remove(book);
        await _db.SaveChangesAsync(ct);
    }
}

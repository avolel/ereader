using System.Globalization;
using EReader.Core.Books;
using EReader.Core.Exceptions;
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
        BookSortKey sortKey,
        SortDirection sortDir,
        BookListFilter filter,
        BookListCursor? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.Books.Where(b => b.UserId == userId);

        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            // Substring, case-insensitive. EF.Functions.ILike translates to Postgres ILIKE.
            // Wildcards in user input are escaped so they match literally.
            var pattern = $"%{EscapeLike(filter.Author)}%";
            query = query.Where(b => EF.Functions.ILike(b.Author, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            var lang = filter.Language;
            query = query.Where(b => b.Language == lang);
        }

        query = ApplySortAndCursor(query, sortKey, sortDir, cursor);

        // Fetch pageSize+1 to know if there's a next page without a separate count.
        var rows = await query.Take(pageSize + 1).ToListAsync(ct);

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

    // Keyset filter (col > value) OR (col = value AND Id > id) keeps pagination
    // deterministic when the sort column has ties. Tie-breaker is always Book.Id.
    // One branch per (sortKey, direction) pair — verbose but trivially translatable
    // and easy to reason about.
    private static IQueryable<Book> ApplySortAndCursor(
        IQueryable<Book> query,
        BookSortKey sortKey,
        SortDirection sortDir,
        BookListCursor? cursor)
    {
        return (sortKey, sortDir) switch
        {
            (BookSortKey.ImportedAt, SortDirection.Desc) => ImportedAtDesc(query, cursor),
            (BookSortKey.ImportedAt, SortDirection.Asc) => ImportedAtAsc(query, cursor),
            (BookSortKey.Title, SortDirection.Asc) => TitleAsc(query, cursor),
            (BookSortKey.Title, SortDirection.Desc) => TitleDesc(query, cursor),
            (BookSortKey.Author, SortDirection.Asc) => AuthorAsc(query, cursor),
            (BookSortKey.Author, SortDirection.Desc) => AuthorDesc(query, cursor),
            _ => throw new ValidationException("Unknown sort key or direction."),
        };
    }

    private static IQueryable<Book> ImportedAtDesc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var ts = ParseTicks(cursor.SortValue);
            var id = cursor.BookId;
            q = q.Where(b => b.ImportedAt < ts || (b.ImportedAt == ts && b.Id < id));
        }
        return q.OrderByDescending(b => b.ImportedAt).ThenByDescending(b => b.Id);
    }

    private static IQueryable<Book> ImportedAtAsc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var ts = ParseTicks(cursor.SortValue);
            var id = cursor.BookId;
            q = q.Where(b => b.ImportedAt > ts || (b.ImportedAt == ts && b.Id > id));
        }
        return q.OrderBy(b => b.ImportedAt).ThenBy(b => b.Id);
    }

    private static IQueryable<Book> TitleAsc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var v = cursor.SortValue;
            var id = cursor.BookId;
            q = q.Where(b => string.Compare(b.Title, v) > 0
                || (b.Title == v && b.Id > id));
        }
        return q.OrderBy(b => b.Title).ThenBy(b => b.Id);
    }

    private static IQueryable<Book> TitleDesc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var v = cursor.SortValue;
            var id = cursor.BookId;
            q = q.Where(b => string.Compare(b.Title, v) < 0
                || (b.Title == v && b.Id < id));
        }
        return q.OrderByDescending(b => b.Title).ThenByDescending(b => b.Id);
    }

    private static IQueryable<Book> AuthorAsc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var v = cursor.SortValue;
            var id = cursor.BookId;
            q = q.Where(b => string.Compare(b.Author, v) > 0
                || (b.Author == v && b.Id > id));
        }
        return q.OrderBy(b => b.Author).ThenBy(b => b.Id);
    }

    private static IQueryable<Book> AuthorDesc(IQueryable<Book> q, BookListCursor? cursor)
    {
        if (cursor is not null)
        {
            var v = cursor.SortValue;
            var id = cursor.BookId;
            q = q.Where(b => string.Compare(b.Author, v) < 0
                || (b.Author == v && b.Id < id));
        }
        return q.OrderByDescending(b => b.Author).ThenByDescending(b => b.Id);
    }

    private static DateTime ParseTicks(string s)
    {
        if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            throw new ValidationException("Invalid cursor.");
        }
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static string EscapeLike(string raw) =>
        raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

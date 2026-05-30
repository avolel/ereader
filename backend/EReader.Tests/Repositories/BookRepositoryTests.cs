using EReader.Core.Books;
using EReader.Core.Models;
using EReader.Data;
using EReader.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EReader.Tests.Repositories;

// Uses EF Core InMemory provider. ILIKE-based author substring filter isn't
// exercised here because the InMemory provider doesn't translate Postgres
// functions — that path needs a real Postgres testcontainer to exercise end-to-end.
// What InMemory does cover: sort directions on every column, keyset cursor logic,
// hasMore signal, user-scoping, and exact-match language filter.
public class BookRepositoryTests : IDisposable
{
    private readonly EReaderDbContext _db;

    public BookRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<EReaderDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new EReaderDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static Book BuildBook(Guid userId, string title, string author, DateTime importedAt, string? language = "en")
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Author = author,
            Language = language,
            FilePath = $"/tmp/{Guid.NewGuid()}.epub",
            FileHash = Guid.NewGuid().ToString("N"),
            FileSize = 1,
            ImportedAt = importedAt,
        };

    private async Task<Book> Seed(Guid userId, string title, string author, DateTime importedAt, string? language = "en")
    {
        var book = BuildBook(userId, title, author, importedAt, language);
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task Should_OrderByImportedAtDescending_When_DefaultSort()
    {
        var userId = Guid.NewGuid();
        var older = await Seed(userId, "Z", "Z", DateTime.UtcNow.AddDays(-2));
        var newer = await Seed(userId, "A", "A", DateTime.UtcNow);

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        items.Select(b => b.Id).Should().Equal(newer.Id, older.Id);
    }

    [Fact]
    public async Task Should_OrderByTitleAscending_When_SortIsTitleAsc()
    {
        var userId = Guid.NewGuid();
        await Seed(userId, "Charlie", "X", DateTime.UtcNow);
        await Seed(userId, "Alpha", "X", DateTime.UtcNow);
        await Seed(userId, "Bravo", "X", DateTime.UtcNow);

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            userId,
            BookSortKey.Title,
            SortDirection.Asc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        items.Select(b => b.Title).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public async Task Should_OrderByAuthorDescending_When_SortIsAuthorDesc()
    {
        var userId = Guid.NewGuid();
        await Seed(userId, "X", "Asimov", DateTime.UtcNow);
        await Seed(userId, "X", "Zelazny", DateTime.UtcNow);
        await Seed(userId, "X", "Martin", DateTime.UtcNow);

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            userId,
            BookSortKey.Author,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        items.Select(b => b.Author).Should().Equal("Zelazny", "Martin", "Asimov");
    }

    [Fact]
    public async Task Should_FilterByLanguageExactly()
    {
        var userId = Guid.NewGuid();
        await Seed(userId, "A", "X", DateTime.UtcNow, language: "en");
        await Seed(userId, "B", "X", DateTime.UtcNow, language: "fr");
        await Seed(userId, "C", "X", DateTime.UtcNow, language: "en");

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            userId,
            BookSortKey.Title,
            SortDirection.Asc,
            new BookListFilter(Author: null, Language: "en"),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        items.Select(b => b.Title).Should().Equal("A", "C");
    }

    [Fact]
    public async Task Should_NotLeakOtherUsersBooks()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await Seed(alice, "Alice's book", "X", DateTime.UtcNow);
        await Seed(bob, "Bob's book", "X", DateTime.UtcNow);

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            alice,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        items.Should().HaveCount(1);
        items[0].Title.Should().Be("Alice's book");
    }

    [Fact]
    public async Task Should_ReturnHasMoreTrue_When_RowCountExceedsPageSize()
    {
        var userId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            await Seed(userId, $"Book{i}", "X", DateTime.UtcNow.AddSeconds(i));
        }

        var repo = new BookRepository(_db);
        var (items, hasMore) = await repo.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 3,
            CancellationToken.None);

        items.Should().HaveCount(3);
        hasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Should_PaginateWithoutOverlap_When_UsingCursor()
    {
        var userId = Guid.NewGuid();
        // ImportedAt distinct per row so the cursor walks rows one-by-one.
        var seeded = new List<Book>();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            seeded.Add(await Seed(userId, $"Book{i}", "X", baseTime.AddMinutes(i)));
        }

        var repo = new BookRepository(_db);
        var (page1, _) = await repo.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 2,
            CancellationToken.None);

        var last = page1[^1];
        var cursor = new BookListCursor(
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            last.ImportedAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            last.Id);

        var (page2, _) = await repo.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor,
            pageSize: 2,
            CancellationToken.None);

        // No overlap between page1 and page2.
        page2.Select(b => b.Id).Should().NotIntersectWith(page1.Select(b => b.Id));
        // Page 1 has the two newest; page 2 has the next two newest.
        var expectedPage2 = seeded
            .OrderByDescending(b => b.ImportedAt)
            .Skip(2)
            .Take(2)
            .Select(b => b.Id);
        page2.Select(b => b.Id).Should().Equal(expectedPage2);
    }

    [Fact]
    public async Task Should_BreakTiesByBookId_When_SortColumnValuesEqual()
    {
        var userId = Guid.NewGuid();
        var sameTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Seed(userId, "A", "X", sameTime);
        await Seed(userId, "B", "X", sameTime);
        await Seed(userId, "C", "X", sameTime);

        var repo = new BookRepository(_db);
        var (items, _) = await repo.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 10,
            CancellationToken.None);

        // All three share ImportedAt; the secondary sort on Id (desc) breaks ties.
        items.Select(b => b.Id).Should().BeInDescendingOrder();
    }
}

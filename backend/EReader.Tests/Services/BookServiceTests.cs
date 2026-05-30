using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.Services;
using FluentAssertions;
using Moq;
using System.Collections.Generic;

namespace EReader.Tests.Services;

public class BookServiceTests
{
    private readonly Mock<IBookRepository> _books = new();
    private readonly Mock<IBookFileStore> _files = new();
    private readonly Mock<IEpubAssetReader> _assets = new();

    private BookService BuildService() => new(_books.Object, _files.Object, _assets.Object);

    private static Book BuildBook(Guid id, Guid userId, string? coverPath = null) => new()
    {
        Id = id,
        UserId = userId,
        Title = "Title",
        Author = "Author",
        FilePath = "/tmp/source.epub",
        FileHash = "hash",
        FileSize = 10,
        ImportedAt = DateTime.UtcNow,
        CoverImagePath = coverPath,
    };

    [Fact]
    public async Task Should_Return404_When_BookMissing()
    {
        _books.Setup(r => r.GetByIdWithChaptersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Book?)null);

        var service = BuildService();

        var act = async () => await service.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_Return404_When_BookBelongsToDifferentUser()
    {
        // The repository's user-scoping is the contract here — the repo returns
        // null for "owned by another user," and the service surfaces a 404
        // (not 403) so existence isn't leaked.
        _books.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Book?)null);

        var service = BuildService();

        var act = async () => await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _books.Verify(
            r => r.RemoveAsync(It.IsAny<Book>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_RewriteRelativeAssetHrefs_When_ReturningChapterContent()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        var chapter = new Chapter
        {
            Id = chapterId,
            BookId = bookId,
            SpineOrder = 1,
            Title = "Ch 2",
            ContentHref = "OEBPS/text/ch2.xhtml",
            ContentText = """<p><img src="../images/cover.png"/><a href="https://external/site">x</a><a href="#anchor">y</a></p>""",
        };

        _books.Setup(r => r.GetChapterAsync(bookId, chapterId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chapter);
        _books.Setup(r => r.GetChapterIdsInSpineOrderAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Guid.NewGuid(), chapterId, Guid.NewGuid() });

        var service = BuildService();

        var result = await service.GetChapterAsync(
            bookId,
            chapterId,
            userId,
            assetBaseUrl: $"/api/v1/books/{bookId}/assets",
            CancellationToken.None);

        result.RewrittenContent.Should().Contain($"/api/v1/books/{bookId}/assets/OEBPS/images/cover.png");
        // External URL stays untouched.
        result.RewrittenContent.Should().Contain("https://external/site");
        // Fragment-only href stays untouched.
        result.RewrittenContent.Should().Contain(@"href=""#anchor""");
    }

    [Fact]
    public async Task Should_ReturnPrevAndNextChapterIds_When_ChapterHasNeighbors()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var prev = Guid.NewGuid();
        var current = Guid.NewGuid();
        var next = Guid.NewGuid();

        var chapter = new Chapter
        {
            Id = current,
            BookId = bookId,
            SpineOrder = 1,
            ContentHref = "OEBPS/ch2.xhtml",
            ContentText = "<p>body</p>",
        };

        _books.Setup(r => r.GetChapterAsync(bookId, current, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chapter);
        _books.Setup(r => r.GetChapterIdsInSpineOrderAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { prev, current, next });

        var service = BuildService();

        var result = await service.GetChapterAsync(bookId, current, userId, "/x", CancellationToken.None);

        result.PreviousChapterId.Should().Be(prev);
        result.NextChapterId.Should().Be(next);
    }

    [Fact]
    public async Task Should_HavePrevNull_When_ChapterIsFirstInSpine()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var chapter = new Chapter
        {
            Id = first,
            BookId = bookId,
            SpineOrder = 0,
            ContentHref = "OEBPS/ch1.xhtml",
            ContentText = string.Empty,
        };

        _books.Setup(r => r.GetChapterAsync(bookId, first, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chapter);
        _books.Setup(r => r.GetChapterIdsInSpineOrderAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { first, second });

        var service = BuildService();

        var result = await service.GetChapterAsync(bookId, first, userId, "/x", CancellationToken.None);

        result.PreviousChapterId.Should().BeNull();
        result.NextChapterId.Should().Be(second);
    }

    [Fact]
    public async Task Should_OrderChaptersBySpine_When_ReturningToc()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var book = BuildBook(bookId, userId);
        // Insert chapters out of order to prove the service re-orders them.
        book.Chapters = new List<Chapter>
        {
            new() { Id = Guid.NewGuid(), BookId = bookId, SpineOrder = 2, Title = "Three" },
            new() { Id = Guid.NewGuid(), BookId = bookId, SpineOrder = 0, Title = "One" },
            new() { Id = Guid.NewGuid(), BookId = bookId, SpineOrder = 1, Title = "Two" },
        };

        _books.Setup(r => r.GetByIdWithChaptersAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        var service = BuildService();

        var result = await service.GetByIdAsync(bookId, userId, CancellationToken.None);

        result.Chapters.Select(c => c.SpineOrder).Should().Equal(0, 1, 2);
        result.Chapters.Select(c => c.Title).Should().Equal("One", "Two", "Three");
    }

    [Fact]
    public async Task Should_DeleteBookAndFiles_When_BookExists()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var book = BuildBook(bookId, userId);

        _books.Setup(r => r.GetByIdAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        var service = BuildService();

        await service.DeleteAsync(bookId, userId, CancellationToken.None);

        _books.Verify(r => r.RemoveAsync(book, It.IsAny<CancellationToken>()), Times.Once);
        _files.Verify(f => f.DeleteForBook(bookId), Times.Once);
    }

    [Fact]
    public async Task Should_Return404_When_CoverPathIsMissingOnDisk()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var book = BuildBook(bookId, userId, coverPath: "/tmp/missing-cover.png");

        _books.Setup(r => r.GetByIdAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);
        _files.Setup(f => f.Exists("/tmp/missing-cover.png")).Returns(false);

        var service = BuildService();

        var act = async () => await service.GetCoverAsync(bookId, userId, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Should_RoundTripCursor_When_EncodingAndDecoding()
    {
        var original = new BookListCursor(
            BookSortKey.Title,
            SortDirection.Asc,
            "Hello: World",  // a literal colon in the value — used to break the old format
            Guid.NewGuid());

        var encoded = BookService.EncodeCursor(original);
        var decoded = BookService.DecodeCursor(encoded);

        decoded.Should().Be(original);
    }

    [Fact]
    public void Should_ThrowValidation_When_DecodingMalformedCursor()
    {
        var act = () => BookService.DecodeCursor("@@@not-base64@@@");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Should_ThrowValidation_When_DecodingStructurallyInvalidCursor()
    {
        // Valid base64 but the decoded payload isn't a valid CursorPayload JSON.
        var bogus = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-a-cursor"));

        var act = () => BookService.DecodeCursor(bogus);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Should_ReturnNull_When_DecodingNullOrEmptyCursor()
    {
        BookService.DecodeCursor(null).Should().BeNull();
        BookService.DecodeCursor(string.Empty).Should().BeNull();
        BookService.DecodeCursor("   ").Should().BeNull();
    }

    [Fact]
    public async Task Should_RejectRequest_When_CursorWasIssuedForADifferentSort()
    {
        // Mint a cursor against Title/Asc, then send it with ImportedAt/Desc.
        // The service must refuse — silently swapping sorts mid-paginate would produce
        // overlapping or missing rows.
        var cursor = BookService.EncodeCursor(new BookListCursor(
            BookSortKey.Title, SortDirection.Asc, "A", Guid.NewGuid()));

        var service = BuildService();

        var act = async () => await service.ListAsync(
            Guid.NewGuid(),
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor,
            20,
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Should_EmitNextCursor_When_RepoSignalsHasMore()
    {
        var userId = Guid.NewGuid();
        var lastBook = BuildBook(Guid.NewGuid(), userId);
        lastBook.Title = "Zebra";

        _books.Setup(r => r.ListAsync(
                userId,
                BookSortKey.Title,
                SortDirection.Asc,
                It.IsAny<BookListFilter>(),
                null,
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Book>)new[] { lastBook }, true));

        var service = BuildService();

        var page = await service.ListAsync(
            userId,
            BookSortKey.Title,
            SortDirection.Asc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 20,
            CancellationToken.None);

        page.NextCursor.Should().NotBeNull();
        // Round-tripping the emitted cursor must yield the last book's keyset position.
        var decoded = BookService.DecodeCursor(page.NextCursor);
        decoded.Should().Be(new BookListCursor(
            BookSortKey.Title, SortDirection.Asc, "Zebra", lastBook.Id));
    }

    [Fact]
    public async Task Should_EmitNoCursor_When_RepoSignalsNoMore()
    {
        var userId = Guid.NewGuid();
        _books.Setup(r => r.ListAsync(
                userId,
                It.IsAny<BookSortKey>(),
                It.IsAny<SortDirection>(),
                It.IsAny<BookListFilter>(),
                It.IsAny<BookListCursor?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Book>)new[] { BuildBook(Guid.NewGuid(), userId) }, false));

        var service = BuildService();

        var page = await service.ListAsync(
            userId,
            BookSortKey.ImportedAt,
            SortDirection.Desc,
            new BookListFilter(null, null),
            cursor: null,
            pageSize: 20,
            CancellationToken.None);

        page.NextCursor.Should().BeNull();
    }
}

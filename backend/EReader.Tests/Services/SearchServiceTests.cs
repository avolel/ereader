using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class SearchServiceTests
{
    private readonly Mock<ISearchRepository> _repo = new();

    private SearchService BuildService() => new(_repo.Object);

    private static SearchHit BuildHit(Guid bookId, Guid chapterId, int spineOrder) =>
        new(
            bookId,
            BookTitle: "Some Book",
            BookAuthor: "Author",
            ChapterId: chapterId,
            ChapterTitle: "Ch",
            ChapterSpineOrder: spineOrder,
            Snippet: "...<mark>hit</mark>...");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]   // 1 char after trim — below MinQueryLength
    public async Task Should_Throw_When_QueryIsTooShort(string? query)
    {
        var service = BuildService();

        var act = async () => await service.SearchAsync(
            Guid.NewGuid(), query!, null, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Should_Throw_When_QueryExceedsMaxLength()
    {
        var service = BuildService();
        var huge = new string('x', 257);

        var act = async () => await service.SearchAsync(
            Guid.NewGuid(), huge, null, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Should_PassTrimmedQueryToRepository()
    {
        _repo.Setup(r => r.SearchAsync(
                It.IsAny<Guid>(),
                "needle",
                null,
                null,
                null,
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<SearchHit>)System.Array.Empty<SearchHit>(), false));

        var service = BuildService();
        await service.SearchAsync(Guid.NewGuid(), "   needle   ", null, null, 20, CancellationToken.None);

        _repo.Verify(r => r.SearchAsync(
            It.IsAny<Guid>(), "needle", null, null, null, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_EmitNextCursor_When_RepoSignalsHasMore()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var lastHit = BuildHit(bookId, Guid.NewGuid(), spineOrder: 42);

        _repo.Setup(r => r.SearchAsync(
                userId, "needle", null, null, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<SearchHit>)new[] { lastHit }, true));

        var service = BuildService();
        var page = await service.SearchAsync(userId, "needle", null, null, 20, CancellationToken.None);

        page.NextCursor.Should().NotBeNull();
        var (decodedBook, decodedSpine) = SearchService.DecodeCursor(page.NextCursor);
        decodedBook.Should().Be(bookId);
        decodedSpine.Should().Be(42);
    }

    [Fact]
    public async Task Should_EmitNoCursor_When_RepoSignalsNoMore()
    {
        _repo.Setup(r => r.SearchAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), null, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<SearchHit>)new[] { BuildHit(Guid.NewGuid(), Guid.NewGuid(), 0) }, false));

        var service = BuildService();
        var page = await service.SearchAsync(Guid.NewGuid(), "needle", null, null, 20, CancellationToken.None);

        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Should_DecodeCursorAndForwardToRepository()
    {
        var userId = Guid.NewGuid();
        var cursorBookId = Guid.NewGuid();
        var cursor = SearchService.EncodeCursor(cursorBookId, 7);

        _repo.Setup(r => r.SearchAsync(
                userId, "needle", null, cursorBookId, 7, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<SearchHit>)System.Array.Empty<SearchHit>(), false));

        var service = BuildService();
        await service.SearchAsync(userId, "needle", null, cursor, 20, CancellationToken.None);

        _repo.Verify(r => r.SearchAsync(
            userId, "needle", null, cursorBookId, 7, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Should_RoundTripCursor_When_EncodingAndDecoding()
    {
        var bookId = Guid.NewGuid();
        var encoded = SearchService.EncodeCursor(bookId, 99);
        var (decodedBook, decodedSpine) = SearchService.DecodeCursor(encoded);
        decodedBook.Should().Be(bookId);
        decodedSpine.Should().Be(99);
    }

    [Fact]
    public void Should_ThrowValidation_When_DecodingMalformedCursor()
    {
        var act = () => SearchService.DecodeCursor("@@@not-base64@@@");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Should_ThrowValidation_When_DecodingStructurallyInvalidCursor()
    {
        var bogus = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-a-cursor"));

        var act = () => SearchService.DecodeCursor(bogus);

        act.Should().Throw<ValidationException>();
    }
}

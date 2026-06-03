using EReader.Core.Annotations;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class AnnotationServiceTests
{
    private readonly Mock<IAnnotationRepository> _repo = new();

    private AnnotationService BuildService() => new(_repo.Object);

    // A well-formed anchor: a JSON object carrying start/end, which is all the service checks.
    private const string ValidAnchor = """{"start":{"path":"/1","offset":0},"end":{"path":"/1","offset":12}}""";

    private static Annotation BuildAnnotation(Guid bookId, Guid userId, AnnotationType type = AnnotationType.Highlight) => new()
    {
        Id = Guid.NewGuid(),
        BookId = bookId,
        UserId = userId,
        Type = type,
        Colour = type == AnnotationType.Highlight ? "yellow" : null,
        TextAnchor = ValidAnchor,
        SelectedText = "some selected text",
        NoteBody = type == AnnotationType.Note ? "a note" : null,
        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
    };

    private static Bookmark BuildBookmark(Guid bookId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        BookId = bookId,
        UserId = userId,
        TextAnchor = ValidAnchor,
        Label = "chapter start",
        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
    };

    private void OwnBook(Guid bookId, Guid userId) =>
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    // ---------- Ownership (404 over 403) ----------

    [Fact]
    public async Task Should_Throw404_When_BookBelongsToDifferentUser()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var create = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, null, "yellow", ValidAnchor, "sel", null),
            CancellationToken.None);
        var list = async () => await BuildService().ListAnnotationsAsync(
            bookId, userId, cursor: null, pageSize: 20, CancellationToken.None);

        await create.Should().ThrowAsync<NotFoundException>();
        await list.Should().ThrowAsync<NotFoundException>();
        _repo.Verify(r => r.AddAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Create: highlights ----------

    [Fact]
    public async Task Should_CreateHighlight_When_ColourValidAndAnchorWellFormed()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var result = await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, null, "YELLOW", ValidAnchor, "sel", null),
            CancellationToken.None);

        result.Type.Should().Be(AnnotationType.Highlight);
        result.Colour.Should().Be("yellow"); // normalised to lower-case
        result.BookId.Should().Be(bookId);
        result.UserId.Should().Be(userId);
        _repo.Verify(r => r.AddAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("purple")] // not in the allow-list
    public async Task Should_RejectHighlight_When_ColourMissingOrInvalid(string? colour)
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var act = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, null, colour, ValidAnchor, "sel", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.AddAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Create: notes ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Should_RejectNote_When_NoteBodyEmpty(string? noteBody)
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var act = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Note, null, null, ValidAnchor, "sel", noteBody),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.AddAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Create: anchor ----------

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]                       // object but no start/end
    [InlineData("""{"start":1}""")]          // missing end
    [InlineData("[1,2,3]")]                  // valid JSON but not an object
    public async Task Should_RejectAnchor_When_NotValidSelectorJson(string anchor)
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var act = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, null, "yellow", anchor, "sel", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Should_RejectAnchor_When_AnchorTooLarge()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        // > MaxAnchorLength (8000): well-formed JSON object with start/end but oversized.
        var huge = $$"""{"start":0,"end":1,"pad":"{{new string('x', 8001)}}"}""";
        var act = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, null, "yellow", huge, "sel", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---------- Create: chapter binding ----------

    [Fact]
    public async Task Should_Reject_When_ChapterDoesNotBelongToBook()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        OwnBook(bookId, userId);
        _repo.Setup(r => r.ChapterBelongsToBookAsync(chapterId, bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await BuildService().CreateAnnotationAsync(
            bookId, userId,
            new CreateAnnotationInput(AnnotationType.Highlight, chapterId, "yellow", ValidAnchor, "sel", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.AddAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Should_Throw404_When_UpdatingAnnotationOfAnotherBook()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var annotationId = Guid.NewGuid();
        // Annotation exists for this user but is anchored to a *different* book.
        var existing = BuildAnnotation(Guid.NewGuid(), userId);
        existing.Id = annotationId;
        _repo.Setup(r => r.GetAnnotationAsync(annotationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var act = async () => await BuildService().UpdateAnnotationAsync(
            bookId, annotationId, userId, new UpdateAnnotationInput("green", null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _repo.Verify(r => r.UpdateAnnotationAsync(It.IsAny<Annotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_OnlyTouchProvidedFields_When_UpdateIsPartial()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = BuildAnnotation(bookId, userId);
        existing.Colour = "yellow";
        existing.NoteBody = "leave me alone";
        _repo.Setup(r => r.GetAnnotationAsync(existing.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await BuildService().UpdateAnnotationAsync(
            bookId, existing.Id, userId,
            new UpdateAnnotationInput(Colour: "GREEN", NoteBody: null),
            CancellationToken.None);

        result.Colour.Should().Be("green");          // updated + normalised
        result.NoteBody.Should().Be("leave me alone"); // untouched
        _repo.Verify(r => r.UpdateAnnotationAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- List paging ----------

    [Fact]
    public async Task Should_ReturnNextCursor_When_RepoReportsHasMore()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);
        var last = BuildAnnotation(bookId, userId);
        _repo.Setup(r => r.ListAnnotationsAsync(
                bookId, userId, It.IsAny<AnnotationCursor?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Annotation> { BuildAnnotation(bookId, userId), last }, true));

        var page = await BuildService().ListAnnotationsAsync(bookId, userId, null, 2, CancellationToken.None);

        page.NextCursor.Should().NotBeNull();
        var decoded = AnnotationService.DecodeCursor(page.NextCursor);
        decoded.Should().Be(new AnnotationCursor(last.CreatedAt, last.Id));
    }

    [Fact]
    public async Task Should_ReturnNullCursor_When_NoMorePages()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);
        _repo.Setup(r => r.ListAnnotationsAsync(
                bookId, userId, It.IsAny<AnnotationCursor?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Annotation> { BuildAnnotation(bookId, userId) }, false));

        var page = await BuildService().ListAnnotationsAsync(bookId, userId, null, 20, CancellationToken.None);

        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public void Should_RoundTripCursor_When_EncodeThenDecode()
    {
        var original = new AnnotationCursor(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Guid.NewGuid());

        var decoded = AnnotationService.DecodeCursor(AnnotationService.EncodeCursor(original));

        decoded.Should().Be(original);
    }

    // ---------- Bookmarks (mirror of the annotation surface) ----------

    [Fact]
    public async Task Should_CreateBookmark_When_AnchorWellFormed()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var result = await BuildService().CreateBookmarkAsync(
            bookId, userId, new CreateBookmarkInput(null, ValidAnchor, "chapter start"), CancellationToken.None);

        result.BookId.Should().Be(bookId);
        result.Label.Should().Be("chapter start");
        _repo.Verify(r => r.AddBookmarkAsync(It.IsAny<Bookmark>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_RejectBookmark_When_LabelTooLong()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        OwnBook(bookId, userId);

        var act = async () => await BuildService().CreateBookmarkAsync(
            bookId, userId,
            new CreateBookmarkInput(null, ValidAnchor, new string('x', 201)), // > MaxLabelLength (200)
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.AddBookmarkAsync(It.IsAny<Bookmark>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Throw404_When_DeletingBookmarkOfAnotherBook()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var bookmarkId = Guid.NewGuid();
        var existing = BuildBookmark(Guid.NewGuid(), userId); // belongs to a different book
        existing.Id = bookmarkId;
        _repo.Setup(r => r.GetBookmarkAsync(bookmarkId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var act = async () => await BuildService().DeleteBookmarkAsync(
            bookId, bookmarkId, userId, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _repo.Verify(r => r.RemoveBookmarkAsync(It.IsAny<Bookmark>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RemoveBookmark_When_OwnedAndOnBook()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = BuildBookmark(bookId, userId);
        _repo.Setup(r => r.GetBookmarkAsync(existing.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await BuildService().DeleteBookmarkAsync(bookId, existing.Id, userId, CancellationToken.None);

        _repo.Verify(r => r.RemoveBookmarkAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }
}

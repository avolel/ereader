using System.Text;
using EReader.Core.Books;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class BookIngestionServiceTests
{
    private readonly Mock<IBookRepository> _books = new();
    private readonly Mock<IBookFileStore> _files = new();
    private readonly Mock<IEpubParser> _parser = new();

    private BookIngestionService BuildService() =>
        new(_books.Object, _files.Object, _parser.Object);

    private static MemoryStream EpubBytes(string content = "PK\x03\x04 epub bytes") =>
        new(Encoding.UTF8.GetBytes(content));

    private static ParsedEpub BuildParsedEpub(int chapterCount = 3, ParsedCover? cover = null)
    {
        var chapters = Enumerable.Range(0, chapterCount)
            .Select(i => new ParsedChapter(
                SpineOrder: i,
                Title: $"Chapter {i + 1}",
                ContentHref: $"OEBPS/text/ch{i + 1}.xhtml",
                ContentText: $"<p>content {i + 1}</p>"))
            .ToList();
        return new ParsedEpub(
            Title: "Test Book",
            Author: "An Author",
            Language: "en",
            Publisher: "Pub",
            PublishedDate: "1900",
            PublishedYear: 1900,
            Description: "desc",
            Cover: cover,
            Chapters: chapters);
    }

    [Fact]
    public async Task Should_RejectUpload_When_FileNameAndContentTypeAreNotEpub()
    {
        var service = BuildService();

        var act = async () => await service.IngestAsync(
            Guid.NewGuid(),
            EpubBytes(),
            fileName: "not-a-book.pdf",
            contentType: "application/pdf",
            CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedFileException>();
    }

    [Fact]
    public async Task Should_AcceptUpload_When_ContentTypeIsEpub()
    {
        _books.Setup(r => r.ExistsByHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _files.Setup(f => f.SaveSourceAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/whatever.epub");
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildParsedEpub());

        var service = BuildService();

        var id = await service.IngestAsync(
            Guid.NewGuid(),
            EpubBytes(),
            fileName: "upload",
            contentType: "application/epub+zip",
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_FileHashAlreadyExistsForUser()
    {
        var userId = Guid.NewGuid();

        _books.Setup(r => r.ExistsByHashAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService();

        var act = async () => await service.IngestAsync(
            userId,
            EpubBytes(),
            fileName: "book.epub",
            contentType: "application/epub+zip",
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "DUPLICATE_BOOK");

        _files.Verify(
            f => f.SaveSourceAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ThrowMalformed_When_ParserFails()
    {
        _books.Setup(r => r.ExistsByHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _files.Setup(f => f.SaveSourceAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/whatever.epub");
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MalformedEpubException("bad opf"));

        var service = BuildService();

        var act = async () => await service.IngestAsync(
            Guid.NewGuid(),
            EpubBytes(),
            fileName: "book.epub",
            contentType: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<MalformedEpubException>();

        // Orphaned file must be cleaned up since no DB row was inserted.
        _files.Verify(f => f.DeleteForBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _books.Verify(
            r => r.AddAsync(It.IsAny<Book>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_PersistChaptersInSpineOrder_When_EpubIsValid()
    {
        _books.Setup(r => r.ExistsByHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _files.Setup(f => f.SaveSourceAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/whatever.epub");
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildParsedEpub(chapterCount: 4));

        IEnumerable<Chapter>? captured = null;
        _books.Setup(r => r.AddAsync(
                It.IsAny<Book>(),
                It.IsAny<IEnumerable<Chapter>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Book, IEnumerable<Chapter>, CancellationToken>((_, chapters, _) =>
                captured = chapters.ToList())
            .Returns(Task.CompletedTask);

        var service = BuildService();
        await service.IngestAsync(
            Guid.NewGuid(),
            EpubBytes(),
            fileName: "book.epub",
            contentType: null,
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Select(c => c.SpineOrder).Should().Equal(0, 1, 2, 3);
        captured.Select(c => c.Title).Should().Equal("Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4");
    }

    [Fact]
    public async Task Should_SaveCover_When_ParsedEpubHasCover()
    {
        var cover = new ParsedCover(new byte[] { 1, 2, 3, 4 }, ".png");

        _books.Setup(r => r.ExistsByHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _files.Setup(f => f.SaveSourceAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/whatever.epub");
        _parser.Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildParsedEpub(cover: cover));
        _files.Setup(f => f.SaveCoverAsync(
                It.IsAny<Guid>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/cover.png");

        var service = BuildService();
        await service.IngestAsync(
            Guid.NewGuid(),
            EpubBytes(),
            fileName: "book.epub",
            contentType: null,
            CancellationToken.None);

        _files.Verify(f => f.SaveCoverAsync(
            It.IsAny<Guid>(),
            It.Is<byte[]>(b => b.Length == 4),
            ".png",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_RejectEmptyUpload_When_StreamHasNoBytes()
    {
        var service = BuildService();

        var act = async () => await service.IngestAsync(
            Guid.NewGuid(),
            new MemoryStream(),
            fileName: "book.epub",
            contentType: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}

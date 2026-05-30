using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.ReadingSettings;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class ReadingSettingsServiceTests
{
    private readonly Mock<IReadingSettingsRepository> _repo = new();

    private ReadingSettingsService BuildService() => new(_repo.Object);

    private static ReadingSetting BuildExisting(Guid userId, Guid? bookId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        BookId = bookId,
        Theme = "light",
        FontFamily = "serif",
        FontSize = 16,
        LineSpacing = 1.5m,
        MarginHorizontal = 40,
        MarginVertical = 20,
        UpdatedAt = DateTime.UtcNow.AddDays(-1),
    };

    // ---------- GET global ----------

    [Fact]
    public async Task Should_ReturnTransientDefaults_When_NoGlobalRowExists()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);

        var result = await BuildService().GetGlobalAsync(userId, CancellationToken.None);

        result.UserId.Should().Be(userId);
        result.BookId.Should().BeNull();
        result.Theme.Should().Be("light");
        result.FontSize.Should().Be(16);
        // Did not persist — Add must never be called for a GET.
        _repo.Verify(r => r.AddAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- GET per-book fallback ----------

    [Fact]
    public async Task Should_FallBackToGlobal_When_PerBookOverrideMissing()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repo.Setup(r => r.GetAsync(userId, bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);
        var global = BuildExisting(userId, bookId: null);
        global.Theme = "dark";
        _repo.Setup(r => r.GetAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(global);

        var result = await BuildService().GetForBookAsync(bookId, userId, CancellationToken.None);

        result.Theme.Should().Be("dark");
        result.BookId.Should().BeNull(); // It's the global row.
    }

    [Fact]
    public async Task Should_Throw404_When_BookBelongsToDifferentUser()
    {
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () =>
            await BuildService().GetForBookAsync(bookId, userId, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------- UPSERT typography ----------

    [Fact]
    public async Task Should_AddNewRow_When_GlobalUpsertHasNoExisting()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);

        var update = new TypographyUpdate("dark", "sans-serif", 18, 1.6m, 60, 30);
        var result = await BuildService().UpsertGlobalAsync(userId, update, CancellationToken.None);

        result.Theme.Should().Be("dark");
        result.FontFamily.Should().Be("sans-serif");
        result.FontSize.Should().Be(18);
        _repo.Verify(r => r.AddAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_PreserveUnsetFields_When_UpsertIsPartial()
    {
        var userId = Guid.NewGuid();
        var existing = BuildExisting(userId, bookId: null);
        existing.FontFamily = "serif";
        existing.FontSize = 16;
        _repo.Setup(r => r.GetAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var update = new TypographyUpdate(Theme: "dark", FontFamily: null, FontSize: null,
            LineSpacing: null, MarginHorizontal: null, MarginVertical: null);
        var result = await BuildService().UpsertGlobalAsync(userId, update, CancellationToken.None);

        result.Theme.Should().Be("dark");
        result.FontFamily.Should().Be("serif");
        result.FontSize.Should().Be(16);
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("midnight")]
    [InlineData("LIGHTER")]
    public async Task Should_RejectInvalidTheme(string theme)
    {
        var userId = Guid.NewGuid();
        var act = async () => await BuildService().UpsertGlobalAsync(
            userId,
            new TypographyUpdate(theme, null, null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(11)] // below MinFontSize=12
    [InlineData(29)] // above MaxFontSize=28
    public async Task Should_RejectOutOfRangeFontSize(int fontSize)
    {
        var userId = Guid.NewGuid();
        var act = async () => await BuildService().UpsertGlobalAsync(
            userId,
            new TypographyUpdate(null, null, fontSize, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Should_NormaliseThemeCasing_When_PersistingValidValue()
    {
        var userId = Guid.NewGuid();
        _repo.Setup(r => r.GetAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);

        var result = await BuildService().UpsertGlobalAsync(
            userId,
            new TypographyUpdate("DARK", null, null, null, null, null),
            CancellationToken.None);

        result.Theme.Should().Be("dark");
    }

    // ---------- DELETE per-book ----------

    [Fact]
    public async Task Should_BeNoop_When_DeletingMissingPerBookOverride()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repo.Setup(r => r.GetAsync(userId, bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);

        await BuildService().DeleteForBookAsync(bookId, userId, CancellationToken.None);

        _repo.Verify(r => r.RemoveAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- POSITION ----------

    [Fact]
    public async Task Should_SeedPerBookRow_When_UpdatingPositionWithNoExistingRow()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repo.Setup(r => r.GetAsync(userId, bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadingSetting?)null);

        var result = await BuildService().UpdatePositionAsync(
            bookId,
            userId,
            new PositionUpdate(chapterId, 240),
            CancellationToken.None);

        result.LastChapterId.Should().Be(chapterId);
        result.LastScrollOffset.Should().Be(240);
        result.LastReadAt.Should().NotBeNull();
        _repo.Verify(r => r.AddAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_UpdatePositionInPlace_When_PerBookRowExists()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var existing = BuildExisting(userId, bookId);
        _repo.Setup(r => r.BookExistsForUserAsync(bookId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repo.Setup(r => r.GetAsync(userId, bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var chapterId = Guid.NewGuid();
        var result = await BuildService().UpdatePositionAsync(
            bookId,
            userId,
            new PositionUpdate(chapterId, 500),
            CancellationToken.None);

        result.LastChapterId.Should().Be(chapterId);
        result.LastScrollOffset.Should().Be(500);
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<ReadingSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RejectNegativeScrollOffset()
    {
        var act = async () => await BuildService().UpdatePositionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new PositionUpdate(Guid.NewGuid(), -1),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
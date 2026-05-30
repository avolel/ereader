using EReader.Api.Dtos;
using EReader.Core.Interfaces;
using EReader.Core.ReadingSettings;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/reading-settings")]
public sealed class ReadingSettingsController : ControllerBase
{
    private readonly IReadingSettingsService _settings;
    private readonly ICurrentUserService _currentUser;

    public ReadingSettingsController(
        IReadingSettingsService settings,
        ICurrentUserService currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    [HttpGet("me")]
    public async Task<ActionResult<ReadingSettingResponse>> GetGlobal(CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var setting = await _settings.GetGlobalAsync(userId, ct);
        return Ok(ReadingSettingResponse.From(setting));
    }

    [HttpPut("me")]
    public async Task<ActionResult<ReadingSettingResponse>> UpsertGlobal(
        [FromBody] UpdateReadingSettingRequest body,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var update = MapUpdate(body);
        var setting = await _settings.UpsertGlobalAsync(userId, update, ct);
        return Ok(ReadingSettingResponse.From(setting));
    }

    [HttpGet("books/{bookId:guid}")]
    public async Task<ActionResult<ReadingSettingResponse>> GetForBook(
        Guid bookId,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var setting = await _settings.GetForBookAsync(bookId, userId, ct);
        return Ok(ReadingSettingResponse.From(setting));
    }

    [HttpPut("books/{bookId:guid}")]
    public async Task<ActionResult<ReadingSettingResponse>> UpsertForBook(
        Guid bookId,
        [FromBody] UpdateReadingSettingRequest body,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var update = MapUpdate(body);
        var setting = await _settings.UpsertForBookAsync(bookId, userId, update, ct);
        return Ok(ReadingSettingResponse.From(setting));
    }

    [HttpDelete("books/{bookId:guid}")]
    public async Task<IActionResult> DeleteForBook(Guid bookId, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        await _settings.DeleteForBookAsync(bookId, userId, ct);
        return NoContent();
    }

    [HttpPut("books/{bookId:guid}/position")]
    public async Task<ActionResult<ReadingSettingResponse>> UpdatePosition(
        Guid bookId,
        [FromBody] UpdateReadingPositionRequest body,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var update = new PositionUpdate(body.ChapterId, body.ScrollOffset);
        var setting = await _settings.UpdatePositionAsync(bookId, userId, update, ct);
        return Ok(ReadingSettingResponse.From(setting));
    }

    private static TypographyUpdate MapUpdate(UpdateReadingSettingRequest body) =>
        new(
            body.Theme,
            body.FontFamily,
            body.FontSize,
            body.LineSpacing,
            body.MarginHorizontal,
            body.MarginVertical);
}
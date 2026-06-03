using EReader.Api.Dtos;
using EReader.Core.Annotations;
using EReader.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/books/{bookId:guid}/bookmarks")]
public sealed class BookmarksController : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IAnnotationService _annotations;
    private readonly ICurrentUserService _currentUser;

    public BookmarksController(IAnnotationService annotations, ICurrentUserService currentUser)
    {
        _annotations = annotations;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<BookmarkListResponse>> List(
        Guid bookId, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var size = pageSize is null or < 1 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize);
        var page = await _annotations.ListBookmarksAsync(bookId, userId, cursor, size, ct);
        return Ok(new BookmarkListResponse(page.Items.Select(BookmarkResponse.From).ToList(), page.NextCursor));
    }

    [HttpPost]
    public async Task<ActionResult<BookmarkResponse>> Create(
        Guid bookId, [FromBody] CreateBookmarkRequest body, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var created = await _annotations.CreateBookmarkAsync(
            bookId, userId, new CreateBookmarkInput(body.ChapterId, body.TextAnchor, body.Label), ct);
        return CreatedAtAction(nameof(List), new { bookId }, BookmarkResponse.From(created));
    }

    [HttpPatch("{bookmarkId:guid}")]
    public async Task<ActionResult<BookmarkResponse>> Update(
        Guid bookId, Guid bookmarkId, [FromBody] UpdateBookmarkRequest body, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var updated = await _annotations.UpdateBookmarkAsync(
            bookId, bookmarkId, userId, new UpdateBookmarkInput(body.Label), ct);
        return Ok(BookmarkResponse.From(updated));
    }

    [HttpDelete("{bookmarkId:guid}")]
    public async Task<IActionResult> Delete(Guid bookId, Guid bookmarkId, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        await _annotations.DeleteBookmarkAsync(bookId, bookmarkId, userId, ct);
        return NoContent();
    }
}

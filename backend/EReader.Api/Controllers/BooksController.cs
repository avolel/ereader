using EReader.Api.Dtos;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/books")]
public sealed class BooksController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IBookService _books;
    private readonly IBookIngestionService _ingestion;
    private readonly ICurrentUserService _currentUser;

    public BooksController(
        IBookService books,
        IBookIngestionService ingestion,
        ICurrentUserService currentUser)
    {
        _books = books;
        _ingestion = ingestion;
        _currentUser = currentUser;
    }

    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<ActionResult<BookDetailResponse>> Upload(
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException("A file is required.");
        }

        var userId = _currentUser.GetCurrentUserId();

        await using var stream = file.OpenReadStream();
        var bookId = await _ingestion.IngestAsync(
            userId,
            stream,
            file.FileName,
            file.ContentType,
            ct);

        var detail = await _books.GetByIdAsync(bookId, userId, ct);
        var response = BookDetailResponse.From(detail, BuildCoverUrl(detail.Book));
        return CreatedAtAction(nameof(GetById), new { bookId }, response);
    }

    [HttpGet]
    public async Task<ActionResult<BookListResponse>> List(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var size = NormalizePageSize(pageSize);

        var page = await _books.ListAsync(userId, cursor, size, ct);
        var items = page.Items
            .Select(b => BookSummary.From(b, BuildCoverUrl(b)))
            .ToList();
        return Ok(new BookListResponse(items, page.NextCursor));
    }

    [HttpGet("{bookId:guid}", Name = nameof(GetById))]
    public async Task<ActionResult<BookDetailResponse>> GetById(
        Guid bookId,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var detail = await _books.GetByIdAsync(bookId, userId, ct);
        return Ok(BookDetailResponse.From(detail, BuildCoverUrl(detail.Book)));
    }

    [HttpGet("{bookId:guid}/chapters/{chapterId:guid}")]
    public async Task<ActionResult<ChapterDetailResponse>> GetChapter(
        Guid bookId,
        Guid chapterId,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var assetBaseUrl = $"/api/v1/books/{bookId}/assets";
        var chapter = await _books.GetChapterAsync(bookId, chapterId, userId, assetBaseUrl, ct);
        return Ok(ChapterDetailResponse.From(chapter));
    }

    [HttpGet("{bookId:guid}/assets/{*assetPath}")]
    public async Task<IActionResult> GetAsset(
        Guid bookId,
        string assetPath,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var asset = await _books.GetAssetAsync(bookId, userId, assetPath, ct);
        // File() takes ownership of the stream and disposes it after the response
        // is flushed — same for /cover below.
        return File(asset.Content, asset.ContentType, asset.FileName);
    }

    [HttpGet("{bookId:guid}/cover")]
    public async Task<IActionResult> GetCover(
        Guid bookId,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var cover = await _books.GetCoverAsync(bookId, userId, ct);
        return File(cover.Content, cover.ContentType, cover.FileName);
    }

    [HttpDelete("{bookId:guid}")]
    public async Task<IActionResult> Delete(Guid bookId, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        await _books.DeleteAsync(bookId, userId, ct);
        return NoContent();
    }

    private static string? BuildCoverUrl(Book book) =>
        string.IsNullOrWhiteSpace(book.CoverImagePath)
            ? null
            : $"/api/v1/books/{book.Id}/cover";

    private static int NormalizePageSize(int? requested)
    {
        if (requested is null || requested < 1) return DefaultPageSize;
        return requested.Value > MaxPageSize ? MaxPageSize : requested.Value;
    }
}

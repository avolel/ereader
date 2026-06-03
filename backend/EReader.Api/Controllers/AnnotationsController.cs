using EReader.Api.Dtos;
using EReader.Core.Annotations;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/books/{bookId:guid}/annotations")]
public sealed class AnnotationsController : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IAnnotationService _annotations;
    private readonly ICurrentUserService _currentUser;

    public AnnotationsController(IAnnotationService annotations, ICurrentUserService currentUser)
    {
        _annotations = annotations;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<AnnotationListResponse>> List(
        Guid bookId, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var size = NormalizePageSize(pageSize);
        var page = await _annotations.ListAnnotationsAsync(bookId, userId, cursor, size, ct);
        var items = page.Items.Select(AnnotationResponse.From).ToList();
        return Ok(new AnnotationListResponse(items, page.NextCursor));
    }

    [HttpPost]
    public async Task<ActionResult<AnnotationResponse>> Create(
        Guid bookId, [FromBody] CreateAnnotationRequest body, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var input = new CreateAnnotationInput(
            ParseType(body.Type), body.ChapterId, body.Colour,
            body.TextAnchor, body.SelectedText, body.NoteBody);
        var created = await _annotations.CreateAnnotationAsync(bookId, userId, input, ct);
        return CreatedAtAction(nameof(List), new { bookId }, AnnotationResponse.From(created));
    }

    [HttpPatch("{annotationId:guid}")]
    public async Task<ActionResult<AnnotationResponse>> Update(
        Guid bookId, Guid annotationId, [FromBody] UpdateAnnotationRequest body, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var updated = await _annotations.UpdateAnnotationAsync(
            bookId, annotationId, userId, new UpdateAnnotationInput(body.Colour, body.NoteBody), ct);
        return Ok(AnnotationResponse.From(updated));
    }

    [HttpDelete("{annotationId:guid}")]
    public async Task<IActionResult> Delete(Guid bookId, Guid annotationId, CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        await _annotations.DeleteAnnotationAsync(bookId, annotationId, userId, ct);
        return NoContent();
    }

    private static AnnotationType ParseType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "highlight" => AnnotationType.Highlight,
        "note" => AnnotationType.Note,
        _ => throw new ValidationException("type must be 'highlight' or 'note'."),
    };

    private static int NormalizePageSize(int? requested)
    {
        if (requested is null || requested < 1) return DefaultPageSize;
        return requested.Value > MaxPageSize ? MaxPageSize : requested.Value;
    }
}

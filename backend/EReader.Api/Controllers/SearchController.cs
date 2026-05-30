using EReader.Api.Dtos;
using EReader.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
public sealed class SearchController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    private readonly ISearchService _search;
    private readonly ICurrentUserService _currentUser;

    public SearchController(ISearchService search, ICurrentUserService currentUser)
    {
        _search = search;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? q,
        [FromQuery(Name = "bookId")] Guid? bookFilter,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var size = NormalizePageSize(pageSize);

        // Query-string validation (empty/too-long) is deferred to the service so the
        // error message and exception type are consistent with other entry points.
        var page = await _search.SearchAsync(userId, q ?? string.Empty, bookFilter, cursor, size, ct);
        var items = page.Items.Select(SearchHitDto.From).ToList();
        return Ok(new SearchResponse(items, page.NextCursor));
    }

    private static int NormalizePageSize(int? requested)
    {
        if (requested is null || requested < 1) return DefaultPageSize;
        return requested.Value > MaxPageSize ? MaxPageSize : requested.Value;
    }
}

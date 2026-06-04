using EReader.Api.Dtos;
using EReader.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/lookup")]
public sealed class LookupController : ControllerBase
{
    private readonly IDictionaryService _dictionary;
    private readonly IWikipediaService _wikipedia;

    public LookupController(IDictionaryService dictionary, IWikipediaService wikipedia)
    {
        _dictionary = dictionary;
        _wikipedia = wikipedia;
    }

    // GET /api/v1/lookup/define?word=ebook  → 200 always (found flag inside body, FR-26)
    [HttpGet("define")]
    public ActionResult<DictionaryResponse> Define([FromQuery] string word)
    {
        var result = _dictionary.Lookup(word);
        return Ok(DictionaryResponse.From(result));
    }

    // GET /api/v1/lookup/wikipedia?term=Project+Gutenberg  → 200 with found flag (FR-27)
    [HttpGet("wikipedia")]
    public async Task<ActionResult<WikipediaResponse>> Wikipedia([FromQuery] string term, CancellationToken ct)
    {
        var result = await _wikipedia.GetSummaryAsync(term, ct);
        return Ok(WikipediaResponse.From(result));
    }
}

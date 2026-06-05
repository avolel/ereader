using EReader.Core.Lookups;

namespace EReader.Core.Interfaces;

public interface IWikipediaService
{
    Task<WikipediaResult> GetSummaryAsync(string term, CancellationToken ct);
}
namespace EReader.Core.Lookups;

public sealed record WikipediaResult(
    string Term,
    bool Found,
    string? Title,
    string? Extract,
    string? PageUrl,
    string? ThumbnailUrl);
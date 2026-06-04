using EReader.Core.Lookups;

namespace EReader.Api.Dtos;

public sealed record DictionarySenseDto(string PartOfSpeech, string Definition, IReadOnlyList<string> Examples);

public sealed record DictionaryResponse(string Word, bool Found, IReadOnlyList<DictionarySenseDto> Senses)
{
    public static DictionaryResponse From(DictionaryResult r) =>
        new(r.Word, r.Found, r.Senses.Select(s => new DictionarySenseDto(s.PartOfSpeech, s.Definition, s.Examples)).ToList());
}

public sealed record WikipediaResponse(string Term, bool Found, string? Title, string? Extract, string? PageUrl, string? ThumbnailUrl)
{
    public static WikipediaResponse From(WikipediaResult r) =>
        new(r.Term, r.Found, r.Title, r.Extract, r.PageUrl, r.ThumbnailUrl);
}
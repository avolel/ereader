using EReader.Core.Books;

namespace EReader.Api.Dtos;

public sealed record SearchHitDto(
    Guid BookId,
    string BookTitle,
    string BookAuthor,
    Guid ChapterId,
    string? ChapterTitle,
    int ChapterSpineOrder,
    string Snippet)
{
    public static SearchHitDto From(SearchHit hit) =>
        new(
            hit.BookId,
            hit.BookTitle,
            hit.BookAuthor,
            hit.ChapterId,
            hit.ChapterTitle,
            hit.ChapterSpineOrder,
            hit.Snippet);
}

public sealed record SearchResponse(IReadOnlyList<SearchHitDto> Items, string? NextCursor);

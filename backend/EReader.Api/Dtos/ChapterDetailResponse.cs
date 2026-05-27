using EReader.Core.Books;

namespace EReader.Api.Dtos;

public sealed record ChapterDetailResponse(
    Guid Id,
    Guid BookId,
    int SpineOrder,
    string? Title,
    string Content,
    Guid? PreviousChapterId,
    Guid? NextChapterId)
{
    public static ChapterDetailResponse From(ChapterContent source) =>
        new(
            source.Chapter.Id,
            source.Chapter.BookId,
            source.Chapter.SpineOrder,
            source.Chapter.Title,
            source.RewrittenContent,
            source.PreviousChapterId,
            source.NextChapterId);
}

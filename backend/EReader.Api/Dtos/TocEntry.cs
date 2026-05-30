using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record TocEntry(Guid ChapterId, int SpineOrder, string? Title)
{
    public static TocEntry From(Chapter chapter) =>
        new(chapter.Id, chapter.SpineOrder, chapter.Title);
}

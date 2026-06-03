using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record BookmarkResponse(
    Guid Id, Guid BookId, Guid? ChapterId, string TextAnchor, string? Label, DateTime CreatedAt)
{
    public static BookmarkResponse From(Bookmark b) =>
        new(b.Id, b.BookId, b.ChapterId, b.TextAnchor, b.Label, b.CreatedAt);
}

public sealed record BookmarkListResponse(IReadOnlyList<BookmarkResponse> Items, string? NextCursor);
public sealed record CreateBookmarkRequest(Guid? ChapterId, string TextAnchor, string? Label);
public sealed record UpdateBookmarkRequest(string? Label);

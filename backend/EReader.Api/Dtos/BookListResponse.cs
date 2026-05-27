namespace EReader.Api.Dtos;

public sealed record BookListResponse(IReadOnlyList<BookSummary> Items, string? NextCursor);

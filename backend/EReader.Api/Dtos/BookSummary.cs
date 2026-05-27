using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record BookSummary(
    Guid Id,
    string Title,
    string Author,
    string? Language,
    string? CoverUrl,
    DateTime ImportedAt)
{
    public static BookSummary From(Book book, string? coverUrl) =>
        new(book.Id, book.Title, book.Author, book.Language, coverUrl, book.ImportedAt);
}

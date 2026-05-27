using EReader.Core.Books;

namespace EReader.Api.Dtos;

public sealed record BookDetailResponse(
    Guid Id,
    string Title,
    string Author,
    string? Language,
    string? Publisher,
    string? PublishedDate,
    int? PublishedYear,
    string? Description,
    string? CoverUrl,
    DateTime ImportedAt,
    IReadOnlyList<TocEntry> Toc)
{
    public static BookDetailResponse From(BookWithChapters source, string? coverUrl)
    {
        var book = source.Book;
        return new BookDetailResponse(
            book.Id,
            book.Title,
            book.Author,
            book.Language,
            book.Publisher,
            book.PublishedDate,
            book.PublishedYear,
            book.Description,
            coverUrl,
            book.ImportedAt,
            source.Chapters.Select(TocEntry.From).ToList());
    }
}

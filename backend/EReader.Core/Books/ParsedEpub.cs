namespace EReader.Core.Books;

public sealed record ParsedEpub(
    string Title,
    string Author,
    string? Language,
    string? Publisher,
    string? PublishedDate,
    int? PublishedYear,
    string? Description,
    ParsedCover? Cover,
    IReadOnlyList<ParsedChapter> Chapters);

public sealed record ParsedCover(byte[] Bytes, string FileExtension);

public sealed record ParsedChapter(
    int SpineOrder,
    string? Title,
    string ContentHref,
    string ContentText);

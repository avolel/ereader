namespace EReader.Core.Books;

public sealed record SearchHit(
    Guid BookId,
    string BookTitle,
    string BookAuthor,
    Guid ChapterId,
    string? ChapterTitle,
    int ChapterSpineOrder,
    // ts_headline output with <mark>...</mark> wrapping the matched terms.
    string Snippet);

public sealed record SearchPage(IReadOnlyList<SearchHit> Items, string? NextCursor);

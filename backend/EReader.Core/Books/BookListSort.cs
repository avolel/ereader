namespace EReader.Core.Books;

public enum BookSortKey
{
    ImportedAt,
    Title,
    Author,
}

public enum SortDirection
{
    Asc,
    Desc,
}

// Cursor decoded into typed form. SortValue is the stringified previous-page
// tail; the repository converts it to the right column type per SortKey.
public sealed record BookListCursor(
    BookSortKey SortKey,
    SortDirection SortDir,
    string SortValue,
    Guid BookId);

public sealed record BookListFilter(string? Author, string? Language);

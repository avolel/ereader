using EReader.Core.Models;

namespace EReader.Core.Books;

public sealed record BookListPage(IReadOnlyList<Book> Items, string? NextCursor);

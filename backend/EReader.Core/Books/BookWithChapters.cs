using EReader.Core.Models;

namespace EReader.Core.Books;

public sealed record BookWithChapters(Book Book, IReadOnlyList<Chapter> Chapters);

using EReader.Core.Models;

namespace EReader.Core.Books;

public sealed record ChapterContent(
    Chapter Chapter,
    string RewrittenContent,
    Guid? PreviousChapterId,
    Guid? NextChapterId);

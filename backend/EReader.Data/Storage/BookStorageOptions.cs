namespace EReader.Data.Storage;

public sealed class BookStorageOptions
{
    public const string SectionName = "Storage";

    // Filesystem root where per-book directories live.
    // Resolved to an absolute path at startup. Each book lives at
    // {BookFilesRoot}/{bookId}/source.epub (+ optional cover.{ext}).
    //
    // Default points one level above the API project (../data/books → backend/data/books)
    // so user uploads land *outside* EReader.Api/ and can't get accidentally git-add-ed.
    // Both that path and the legacy EReader.Api/data/ are gitignored as a safety net.
    public string BookFilesRoot { get; set; } = "../data/books";
}

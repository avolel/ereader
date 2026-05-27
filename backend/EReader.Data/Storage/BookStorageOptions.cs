namespace EReader.Data.Storage;

public sealed class BookStorageOptions
{
    public const string SectionName = "Storage";

    // Filesystem root where per-book directories live.
    // Resolved to an absolute path at startup. Each book lives at
    // {BookFilesRoot}/{bookId}/source.epub (+ optional cover.{ext}).
    public string BookFilesRoot { get; set; } = "./data/books";
}

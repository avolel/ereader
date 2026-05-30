namespace EReader.Data.Storage;

public sealed class BookStorageOptions
{
    public const string SectionName = "Storage";

    // Filesystem root where per-book directories live.
    // Resolved to an absolute path at startup. Each book lives at
    // {BookFilesRoot}/{bookId}/source.epub (+ optional cover.{ext}).
    //
    // Defaults to the current user's home directory ($HOME/data/books) so uploads
    // land outside the repo and the path is portable across machines/users.
    public string BookFilesRoot { get; set; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "data",
    "books");
}

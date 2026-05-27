namespace EReader.Core.Interfaces;

// Filesystem (or future S3) adapter for original EPUBs and extracted covers.
// All paths returned/accepted are absolute as stored on Book.FilePath / Book.CoverImagePath.
public interface IBookFileStore
{
    Task<string> SaveSourceAsync(Guid bookId, Stream contents, CancellationToken ct);

    Task<string> SaveCoverAsync(Guid bookId, byte[] bytes, string fileExtension, CancellationToken ct);

    Stream OpenRead(string absolutePath);

    bool Exists(string absolutePath);

    void DeleteForBook(Guid bookId);
}

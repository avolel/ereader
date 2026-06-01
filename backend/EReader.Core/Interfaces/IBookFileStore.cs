namespace EReader.Core.Interfaces;

// Object-storage adapter (MinIO) for original EPUBs and extracted covers.
// Paths returned/accepted are MinIO object KEYS, stored on
// Book.FilePath / Book.CoverImagePath (e.g. "{bookId}/source.epub").
public interface IBookFileStore
{
    Task<string> SaveSourceAsync(Guid bookId, Stream contents, CancellationToken ct);

    Task<string> SaveCoverAsync(Guid bookId, byte[] bytes, string fileExtension, CancellationToken ct);

    // Returns a SEEKABLE in-memory stream (the object is buffered) — callers
    // (cover streaming, zip asset reader) need random access. Caller disposes.
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);

    Task<bool> ExistsAsync(string objectKey, CancellationToken ct);

    Task DeleteForBookAsync(Guid bookId, CancellationToken ct);
}

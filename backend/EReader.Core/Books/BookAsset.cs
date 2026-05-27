namespace EReader.Core.Books;

// Stream is owned by the caller — it must be disposed after use.
public sealed record BookAsset(Stream Content, string ContentType, long Length, string? FileName = null);

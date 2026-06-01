using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface IEpubAssetReader
{
    // epubStream must be seekable; the returned BookAsset's stream takes ownership
    // of it and disposes it along with the underlying ZipArchive.
    BookAsset? OpenAsset(Stream epubStream, string assetPath);
}

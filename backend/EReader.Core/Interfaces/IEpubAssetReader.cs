using EReader.Core.Books;

namespace EReader.Core.Interfaces;

// Streams a single asset (image/css/font) out of the EPUB zip on demand.
// Returns null if the asset is not in the archive.
// The returned BookAsset's stream is owned by the caller and must be disposed
// — disposing it also disposes the underlying ZipArchive entry.
public interface IEpubAssetReader
{
    BookAsset? OpenAsset(string epubPath, string assetPath);
}

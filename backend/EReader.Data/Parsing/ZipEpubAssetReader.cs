using System.IO.Compression;
using EReader.Core.Books;
using EReader.Core.Interfaces;

namespace EReader.Data.Parsing;

// Reads a single asset (image/css/font/anything) out of the EPUB zip without
// loading the whole archive into memory. The returned stream owns the ZipArchive
// — disposing it disposes the archive too, so callers must dispose the stream.
public sealed class ZipEpubAssetReader : IEpubAssetReader
{
    public BookAsset? OpenAsset(string epubPath, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;

        // Normalize: strip leading slash + back-slashes (EPUB internal paths
        // are forward-slash, but a careless client might send either form).
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');

        // ZipFile.OpenRead opens with FileShare.Read so concurrent asset reads
        // for the same book are fine.
        var archive = ZipFile.OpenRead(epubPath);
        try
        {
            // Case-insensitive lookup: EPUBs vary, and some manifest paths
            // differ in case from the zip entry names.
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, normalized, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                archive.Dispose();
                return null;
            }

            var entryStream = entry.Open();
            // Wrap so disposing the returned stream tears down the archive too.
            var owning = new ArchiveOwningStream(entryStream, archive);
            return new BookAsset(
                Content: owning,
                ContentType: ContentTypeFor(entry.FullName),
                Length: entry.Length,
                FileName: Path.GetFileName(entry.FullName));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static string ContentTypeFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" or ".xhtml" => "application/xhtml+xml",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp3" => "audio/mpeg",
            ".mp4" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".json" => "application/json",
            ".xml" => "application/xml",
            _ => "application/octet-stream",
        };
    }

    private sealed class ArchiveOwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly ZipArchive _archive;
        private bool _disposed;

        public ArchiveOwningStream(Stream inner, ZipArchive archive)
        {
            _inner = inner;
            _archive = archive;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _inner.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _inner.ReadAsync(buffer, ct);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                _inner.Dispose();
                _archive.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

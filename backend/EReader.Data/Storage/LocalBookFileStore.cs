using EReader.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace EReader.Data.Storage;

public sealed class LocalBookFileStore : IBookFileStore
{
    private readonly string _root;

    public LocalBookFileStore(IOptions<BookStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.BookFilesRoot);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveSourceAsync(Guid bookId, Stream contents, CancellationToken ct)
    {
        var dir = BookDirectory(bookId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "source.epub");

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (contents.CanSeek) contents.Position = 0;
        await contents.CopyToAsync(fs, ct);
        return path;
    }

    public async Task<string> SaveCoverAsync(Guid bookId, byte[] bytes, string fileExtension, CancellationToken ct)
    {
        var dir = BookDirectory(bookId);
        Directory.CreateDirectory(dir);
        var ext = NormalizeExtension(fileExtension);
        var path = Path.Combine(dir, $"cover{ext}");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    public Stream OpenRead(string absolutePath) =>
        new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);

    public bool Exists(string absolutePath) => File.Exists(absolutePath);

    public void DeleteForBook(Guid bookId)
    {
        var dir = BookDirectory(bookId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private string BookDirectory(Guid bookId) => Path.Combine(_root, bookId.ToString());

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}

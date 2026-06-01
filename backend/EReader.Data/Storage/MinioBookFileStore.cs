using EReader.Core.Interfaces;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace EReader.Data.Storage;

public sealed class MinioBookFileStore : IBookFileStore
{
    private readonly IMinioClient _client;
    private readonly string _bucket;

    public MinioBookFileStore(IMinioClient client, IOptions<MinioOptions> options)
    {
        _client = client;
        _bucket = options.Value.Bucket;
    }

    public async Task DeleteForBookAsync(Guid bookId, CancellationToken ct)
    {
        var prefix = $"{bookId}/";
        var keys = new List<string>();
        await foreach (var item in _client.ListObjectsEnumAsync(new ListObjectsArgs()
            .WithBucket(_bucket).WithPrefix(prefix).WithRecursive(true), ct))
        {
            keys.Add(item.Key);
        }
        if (keys.Count == 0) return;
        await _client.RemoveObjectsAsync(new RemoveObjectsArgs()
            .WithBucket(_bucket).WithObjects(keys), ct);
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_bucket).WithObject(objectKey), ct);
            return true;
        }
        catch (ObjectNotFoundException) { return false; }
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        // Buffer into memory so the caller gets a seekable stream (VersOne/ZipArchive
        // need random access). EPUBs are a few MB — consistent with the in-memory
        // buffering the ingestion service already does.
        var ms = new MemoryStream();
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithCallbackStream((s, _) => s.CopyToAsync(ms)), ct);
        ms.Position = 0;
        return ms;
    }

    public async Task<string> SaveCoverAsync(Guid bookId, byte[] bytes, string fileExtension, CancellationToken ct)
    {
        var key = $"{bookId}/cover{NormalizeExtension(fileExtension)}";
        using var ms = new MemoryStream(bytes);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(ms)
            .WithObjectSize(bytes.Length), ct);
        return key;
    }

    public async Task<string> SaveSourceAsync(Guid bookId, Stream contents, CancellationToken ct)
    {
        var key = $"{bookId}/source.epub";
        if (contents.CanSeek) contents.Position = 0;
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(contents)
            .WithObjectSize(contents.CanSeek ? contents.Length : -1)
            .WithContentType("application/epub+zip"), ct);
        return key;
    }

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}

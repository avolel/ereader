using System.Security.Cryptography;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;

namespace EReader.Core.Services;

public sealed class BookIngestionService : IBookIngestionService
{
    private readonly IBookRepository _books;
    private readonly IBookFileStore _files;
    private readonly IEpubParser _parser;

    public BookIngestionService(
        IBookRepository books,
        IBookFileStore files,
        IEpubParser parser)
    {
        _books = books;
        _files = files;
        _parser = parser;
    }

    public async Task<Guid> IngestAsync(
        Guid userId,
        Stream contents,
        string fileName,
        string? contentType,
        CancellationToken ct)
    {
        ValidateUpload(fileName, contentType);

        // Read once into memory so we can both hash and save without seeking
        // a non-seekable upload stream twice. Phase 1 EPUBs are a few MB —
        // acceptable. Worth revisiting if we ever ingest large files.
        await using var buffered = new MemoryStream();
        await contents.CopyToAsync(buffered, ct);
        if (buffered.Length == 0)
        {
            throw new ValidationException("Uploaded file is empty.");
        }

        buffered.Position = 0;
        var fileHash = await ComputeSha256Async(buffered, ct);

        if (await _books.ExistsByHashAsync(userId, fileHash, ct))
        {
            throw new ConflictException("This EPUB has already been imported.", "DUPLICATE_BOOK");
        }

        var bookId = Guid.NewGuid();

        buffered.Position = 0;
        var savedKey = await _files.SaveSourceAsync(bookId, buffered, ct);

        try
        {
            // VersOne's EpubReader disposes the stream it's given, so hand it an
            // isolated copy and keep `buffered` under this method's sole ownership.
            buffered.Position = 0;
            using var parseStream = new MemoryStream(buffered.ToArray());
            var parsed = await _parser.ParseAsync(parseStream, ct);

            string? coverKey = null;
            if (parsed.Cover is { } cover)
            {
                coverKey = await _files.SaveCoverAsync(bookId, cover.Bytes, cover.FileExtension, ct);
            }

            var book = new Book
            {
                Id = bookId,
                UserId = userId,
                Title = parsed.Title,
                Author = parsed.Author,
                Language = parsed.Language,
                Publisher = parsed.Publisher,
                PublishedDate = parsed.PublishedDate,
                PublishedYear = parsed.PublishedYear,
                Description = parsed.Description,
                FilePath = savedKey,
                FileHash = fileHash,
                FileSize = buffered.Length,
                CoverImagePath = coverKey,
                ImportedAt = DateTime.UtcNow,
            };

            var chapters = parsed.Chapters.Select(c => new Chapter
            {
                Id = Guid.NewGuid(),
                BookId = bookId,
                SpineOrder = c.SpineOrder,
                Title = c.Title,
                ContentHref = c.ContentHref,
                ContentText = c.ContentText,
            }).ToList();

            await _books.AddAsync(book, chapters, ct);
            return bookId;
        }
        catch
        {
            await _files.DeleteForBookAsync(bookId, ct);
            throw;
        }
    }

    private static void ValidateUpload(string fileName, string? contentType)
    {
        var hasEpubExtension = fileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);
        var hasEpubContentType = string.Equals(
            contentType,
            "application/epub+zip",
            StringComparison.OrdinalIgnoreCase);

        if (!hasEpubExtension && !hasEpubContentType)
        {
            throw new UnsupportedFileException("Only EPUB files are accepted.");
        }
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

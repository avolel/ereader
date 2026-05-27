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
        var savedPath = await _files.SaveSourceAsync(bookId, buffered, ct);

        try
        {
            var parsed = await _parser.ParseAsync(savedPath, ct);

            string? coverPath = null;
            if (parsed.Cover is { } cover)
            {
                coverPath = await _files.SaveCoverAsync(bookId, cover.Bytes, cover.FileExtension, ct);
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
                FilePath = savedPath,
                FileHash = fileHash,
                FileSize = buffered.Length,
                CoverImagePath = coverPath,
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
            // Persisting to disk happened before we knew the parse/save would
            // succeed. If anything downstream blows up, clean the orphaned files
            // so we don't leak storage. The DB row was never inserted (AddAsync
            // is the last step), so there's nothing else to roll back.
            _files.DeleteForBook(bookId);
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

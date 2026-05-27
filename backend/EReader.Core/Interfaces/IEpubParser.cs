using EReader.Core.Books;

namespace EReader.Core.Interfaces;

public interface IEpubParser
{
    Task<ParsedEpub> ParseAsync(string epubPath, CancellationToken ct);
}

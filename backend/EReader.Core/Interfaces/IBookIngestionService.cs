namespace EReader.Core.Interfaces;

public interface IBookIngestionService
{
    // Returns the new Book.Id. The stream is read once (hashed + persisted).
    Task<Guid> IngestAsync(
        Guid userId,
        Stream contents,
        string fileName,
        string? contentType,
        CancellationToken ct);
}

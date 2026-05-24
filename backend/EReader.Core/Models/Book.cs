namespace EReader.Core.Models;

public class Book
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? Publisher { get; set; }
    // EPUB OPF dates are inconsistent (year only, partial dates, "circa", etc.),
    // so the raw value is kept as a string. PublishedYear is a parsed-out int for sorting/filtering.
    public string? PublishedDate { get; set; }
    public int? PublishedYear { get; set; }
    public string? Description { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? CoverImagePath { get; set; }
    public DateTime ImportedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Chapter> Chapters { get; set; } = [];
    public ICollection<Annotation> Annotations { get; set; } = [];
    public ICollection<Bookmark> Bookmarks { get; set; } = [];
    public ICollection<ReadingSetting> ReadingSettings { get; set; } = [];
}

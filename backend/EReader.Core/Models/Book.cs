namespace EReader.Core.Models;

public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public int? LastReadPosition { get; set; }
}

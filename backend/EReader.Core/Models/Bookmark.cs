namespace EReader.Core.Models;

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public Guid UserId { get; set; }
    public string TextAnchor { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }

    public Book Book { get; set; } = null!;
    public Chapter? Chapter { get; set; }
    public User User { get; set; } = null!;
}

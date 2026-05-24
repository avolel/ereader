namespace EReader.Core.Models;

public class Annotation
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid UserId { get; set; }
    public AnnotationType Type { get; set; }
    public string? Colour { get; set; }
    public string TextAnchor { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    public string? NoteBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Book Book { get; set; } = null!;
    public Chapter Chapter { get; set; } = null!;
    public User User { get; set; } = null!;
}

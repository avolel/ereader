namespace EReader.Core.Models;

public class Chapter
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public int SpineOrder { get; set; }
    public string? Title { get; set; }
    public string ContentHref { get; set; } = string.Empty;
    public string? ContentText { get; set; }

    public Book Book { get; set; } = null!;
    public ICollection<Annotation> Annotations { get; set; } = [];
    public ICollection<Bookmark> Bookmarks { get; set; } = [];
}

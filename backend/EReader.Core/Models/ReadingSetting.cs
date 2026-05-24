namespace EReader.Core.Models;

public class ReadingSetting
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BookId { get; set; }
    public string Theme { get; set; } = "light";
    public string FontFamily { get; set; } = "serif";
    public int FontSize { get; set; } = 16;
    public decimal LineSpacing { get; set; } = 1.5m;
    public int MarginHorizontal { get; set; } = 40;
    public int MarginVertical { get; set; } = 20;
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Book? Book { get; set; }
}

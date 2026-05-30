namespace EReader.Core.ReadingSettings;

// Typography + theme. All fields optional on update — null means "leave as is."
// Validation lives in the service so the same rules apply whether the input came
// from the API or from a future internal caller.
public sealed record TypographyUpdate(
    string? Theme,
    string? FontFamily,
    int? FontSize,
    decimal? LineSpacing,
    int? MarginHorizontal,
    int? MarginVertical);

// Reading position is a separate concept from typography: position is always
// per-book, never global, and updates a lot more often. Splitting the input
// type keeps the position-update path narrow and clear.
public sealed record PositionUpdate(Guid ChapterId, int ScrollOffset);
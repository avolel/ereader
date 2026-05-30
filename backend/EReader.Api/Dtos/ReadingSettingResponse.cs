using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record ReadingSettingResponse(
    Guid? BookId,
    string Theme,
    string FontFamily,
    int FontSize,
    decimal LineSpacing,
    int MarginHorizontal,
    int MarginVertical,
    Guid? LastChapterId,
    int LastScrollOffset,
    DateTime? LastReadAt,
    DateTime UpdatedAt)
{
    public static ReadingSettingResponse From(ReadingSetting source) =>
        new(
            source.BookId,
            source.Theme,
            source.FontFamily,
            source.FontSize,
            source.LineSpacing,
            source.MarginHorizontal,
            source.MarginVertical,
            source.LastChapterId,
            source.LastScrollOffset,
            source.LastReadAt,
            source.UpdatedAt);
}

// All fields nullable: a PATCH-style upsert where null means "don't touch."
public sealed record UpdateReadingSettingRequest(
    string? Theme,
    string? FontFamily,
    int? FontSize,
    decimal? LineSpacing,
    int? MarginHorizontal,
    int? MarginVertical);

public sealed record UpdateReadingPositionRequest(Guid ChapterId, int ScrollOffset);
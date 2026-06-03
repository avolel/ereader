using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record AnnotationResponse(
    Guid Id,
    Guid BookId,
    Guid? ChapterId,
    string Type,            // "highlight" | "note"
    string? Colour,
    string TextAnchor,
    string SelectedText,
    string? NoteBody,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static AnnotationResponse From(Annotation a) =>
        new(a.Id, a.BookId, a.ChapterId, a.Type.ToString().ToLowerInvariant(),
            a.Colour, a.TextAnchor, a.SelectedText, a.NoteBody, a.CreatedAt, a.UpdatedAt);
}

public sealed record AnnotationListResponse(IReadOnlyList<AnnotationResponse> Items, string? NextCursor);

// type: "highlight" | "note". Parsed/validated in the controller into AnnotationType.
public sealed record CreateAnnotationRequest(
    string Type, Guid? ChapterId, string? Colour,
    string TextAnchor, string SelectedText, string? NoteBody);

public sealed record UpdateAnnotationRequest(string? Colour, string? NoteBody);

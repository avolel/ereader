using EReader.Core.Models;
namespace EReader.Core.Annotations;

// Create input. TextAnchor is opaque (frontend selector JSON); the backend stores it
// verbatim. ChapterId is persisted separately so list/filter never parses the anchor.
public sealed record CreateAnnotationInput(
    AnnotationType Type,
    Guid? ChapterId,
    string? Colour,
    string TextAnchor,
    string SelectedText,
    string? NoteBody);

// PATCH-style: null means "leave as is". Only colour (highlights) and note body are
// mutable — anchor/selected text are immutable once captured.
public sealed record UpdateAnnotationInput(string? Colour, string? NoteBody);

public sealed record CreateBookmarkInput(Guid? ChapterId, string TextAnchor, string? Label);

public sealed record UpdateBookmarkInput(string? Label);

// Keyset page (CreatedAt desc, Id desc). NextCursor is opaque base64url JSON.
public sealed record AnnotationPage(IReadOnlyList<Annotation> Items, string? NextCursor);
public sealed record BookmarkPage(IReadOnlyList<Bookmark> Items, string? NextCursor);

// Decoded keyset position. CreatedAt is stored as ticks in the encoded cursor.
public sealed record AnnotationCursor(DateTime CreatedAt, Guid Id);

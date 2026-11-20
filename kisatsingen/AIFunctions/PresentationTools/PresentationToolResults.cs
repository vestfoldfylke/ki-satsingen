namespace kisatsingen.AIFunctions.PresentationTools;

// What the model receives, serialized in camelCase with null fields left out.
// The UI reads FileName and SlideCount back for the tool call's title, and
// Pictures for the picture list's, so the shapes are pinned by tests.
internal static class PresentationToolResults
{
    public sealed record PresentationCreatedResult(string FileName, int SlideCount, string Notice);

    public sealed record PresentationPictureEntry(string PictureId, string Description);

    // Notice is set only when there are no pictures, to tell the model why.
    public sealed record PresentationPicturesResult(IReadOnlyList<PresentationPictureEntry> Pictures, string? Notice);
}

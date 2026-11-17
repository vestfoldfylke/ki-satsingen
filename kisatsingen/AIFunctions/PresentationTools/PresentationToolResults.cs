namespace kisatsingen.AIFunctions.PresentationTools;

// What the model receives, serialized in camelCase with null fields left out.
// The UI reads FileName and SlideCount back for the tool call's title, so the
// shape is pinned by tests.
internal static class PresentationToolResults
{
    public sealed record PresentationCreatedResult(string FileName, int SlideCount, string Notice);
}

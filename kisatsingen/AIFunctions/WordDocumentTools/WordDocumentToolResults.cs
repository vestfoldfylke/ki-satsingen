namespace kisatsingen.AIFunctions.WordDocumentTools;

// What the model receives, serialized in camelCase with null fields left out.
// The UI reads FileName back for the tool call's title, so the shapes are
// pinned by tests.
internal static class WordDocumentToolResults
{
    public sealed record WordDocumentCreatedResult(string FileName, int BlockCount, string Notice);

    public sealed record MeetingDocumentCreatedResult(string FileName, int AgendaItemCount, string Notice);
}

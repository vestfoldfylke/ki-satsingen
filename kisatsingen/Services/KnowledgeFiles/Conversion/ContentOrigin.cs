using System.Diagnostics;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Where a file's Markdown comes from: what was uploaded and, when that is not
// enough, how its text was obtained. A plain fact about the file; how far to
// trust it is a note derived from this when the file is shown, so the wording
// can improve without touching stored data.
//
// Recorded rather than derived from the content type: once one type has two
// routes (a PDF's text layer or OCR), files
// converted one way could never be told apart from the others, since
// originals are not kept.
//
// Persisted by name. A stored name that is renamed or removed reads back as
// unknown, so those files get the cautious note instead of their own; migrate
// the stored rows if that matters.
public enum ContentOrigin
{
    // The upload was a text file (.txt, .md), used as it is: exact.
    TextFile,

    // The upload was a PDF file (*.pdf), whose content has been converted to Markdown.
    Pdf,

    // The upload was a Word document (*.docx) whose content has been converted to Markdown.
    Docx,

    // The upload was an image, described and transcribed by a vision model.
    ImageDescription
}

// Shown to both the model and the user, so a note states how the text was
// obtained, never what to do about it.
public static class ContentOriginNotes
{
    public static string? For(ContentOrigin? origin) => origin switch
    {
        ContentOrigin.TextFile => null,
        ContentOrigin.Docx or ContentOrigin.Pdf => "Teksten er hentet ut og konvertert til Markdown.",
        ContentOrigin.ImageDescription => "Teksten og beskrivelsen er laget av en KI-modell ut fra bildet, og kan inneholde feil.",
        // A stored name this build does not know.
        null => "Det er ukjent hvordan teksten ble hentet ut, så den kan være unøyaktig.",
        _ => throw new UnreachableException($"ContentOrigin {origin} has no note. Add one here in the same change that adds the value.")
    };
}

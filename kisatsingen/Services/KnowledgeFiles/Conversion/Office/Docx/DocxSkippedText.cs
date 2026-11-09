using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Elements whose text is never output. Everything else is walked into, so
// wrappers such as content controls, smart tags and inserted text keep their
// text without code of their own.
//
// Deleted text (w:delText) and field codes (w:instrText) need no entry: they
// are elements of their own, not w:t, so their text is never read.
internal static class DocxSkippedText
{
    public static bool IsSkipped(OpenXmlElement element) => element switch
    {
        // Tracked changes are read as accepted: deleted and moved-away runs are
        // gone, with their tabs and breaks.
        DeletedRun or MoveFromRun => true,
        Run run => IsHidden(run.RunProperties),
        // The prompt Word shows in an empty content control ("Klikk her for å skrive inn tekst").
        SdtElement contentControl => DocxToggle.Read(contentControl.SdtProperties?.GetFirstChild<ShowingPlaceholder>()) == true,
        _ => false
    };

    // webHidden hides text only on screen; Word uses it for page numbers in a table of contents.
    private static bool IsHidden(RunProperties? runProperties) =>
        DocxToggle.Read(runProperties?.Vanish) == true || DocxToggle.Read(runProperties?.WebHidden) == true;
}

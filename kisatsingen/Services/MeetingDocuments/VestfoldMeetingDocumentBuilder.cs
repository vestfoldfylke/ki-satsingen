using DocumentFormat.OpenXml.Packaging;
using kisatsingen.Services.WordDocuments;

namespace kisatsingen.Services.MeetingDocuments;

// Turns a validated spec into a .docx built on the Vestfold meeting notice and
// minutes template, a landscape form. The same spec gives the same document;
// only the file's created time differs.
public sealed class VestfoldMeetingDocumentBuilder(WordTemplateFiles templateFiles, TimeProvider timeProvider)
{
    public const string FallbackFileStem = "møte";

    public ArraySegment<byte> Build(MeetingDocumentSpec spec) =>
        WordTemplateBuild.Run(
            templateFiles.OpenWritableCopy(WordTemplateFile.Meeting),
            spec.Title,
            timeProvider.GetUtcNow().UtcDateTime,
            mainPart => Fill(mainPart, spec));

    private static void Fill(MainDocumentPart mainPart, MeetingDocumentSpec spec)
    {
        var body = mainPart.Document?.Body
            ?? throw new InvalidOperationException("The meeting Word template has no document body; the template file is damaged.");

        MeetingForm.Fill(body, spec);
        AgendaTable.Fill(body, spec.AgendaItems);
    }
}

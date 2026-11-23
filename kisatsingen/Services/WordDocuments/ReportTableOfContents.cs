using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The report's table of contents is a field only Word can fill, since only
// Word lays out pages. Its example entries are replaced by a note, and the
// document asks Word to update its fields when it opens.
internal static class ReportTableOfContents
{
    public const string PendingText = "Innholdsfortegnelsen oppdateres når dokumentet åpnes i Word.";

    private const string TableOfContentsInstruction = "TOC";

    public static void Reset(SdtBlock tableOfContents, MainDocumentPart mainPart)
    {
        var content = tableOfContents.SdtContentBlock
            ?? throw TemplateChanged("an empty table of contents");
        var fieldParagraph = content.Elements<Paragraph>().FirstOrDefault(IsFieldStart)
            ?? throw TemplateChanged("a table of contents without its TOC field");
        var instruction = fieldParagraph.Descendants<FieldCode>().First(IsTableOfContentsCode).Text;

        foreach (var paragraph in fieldParagraph.ElementsAfter().ToList())
        {
            paragraph.Remove();
        }

        fieldParagraph.InsertAfterSelf(CreatePendingField(fieldParagraph.ParagraphProperties, instruction));
        fieldParagraph.Remove();

        AskWordToUpdateFields(mainPart);
    }

    // Dirty marks the field stale, so Word updates it with the others.
    private static Paragraph CreatePendingField(ParagraphProperties? properties, string instruction)
    {
        var paragraph = new Paragraph();
        if (properties?.CloneNode(deep: true) is { } clonedProperties)
        {
            paragraph.Append(clonedProperties);
        }

        paragraph.Append(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
            new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text(PendingText)),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
        return paragraph;
    }

    // Word asks the user once, as the file opens, whether to update them.
    private static void AskWordToUpdateFields(MainDocumentPart mainPart)
    {
        var settings = mainPart.DocumentSettingsPart?.Settings
            ?? throw TemplateChanged("no document settings");

        settings.GetFirstChild<UpdateFieldsOnOpen>()?.Remove();
        settings.AddChild(new UpdateFieldsOnOpen { Val = true });
    }

    private static bool IsFieldStart(Paragraph paragraph) => paragraph.Descendants<FieldCode>().Any(IsTableOfContentsCode);

    private static bool IsTableOfContentsCode(FieldCode code) =>
        code.Text.TrimStart().StartsWith(TableOfContentsInstruction + " ", StringComparison.Ordinal);

    private static InvalidOperationException TemplateChanged(string what) =>
        new($"The report Word template has {what}. The template changed; update {nameof(ReportTableOfContents)} to match it.");
}

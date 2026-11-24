using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The report's table of contents is a field only Word can fill, since only
// Word lays out pages. Its example entries are replaced by a note telling the
// user how to update it. Asking Word to update fields on open is avoided: Word
// then shows a warning about fields referring to other files, which users
// often decline.
internal static class ReportTableOfContents
{
    private const string PendingText = "Høyreklikk her og velg Oppdater felt for å lage innholdsfortegnelsen.";

    private const string TableOfContentsInstruction = "TOC";

    public static void Reset(SdtBlock tableOfContents)
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
    }

    private static Paragraph CreatePendingField(ParagraphProperties? properties, string instruction)
    {
        var paragraph = new Paragraph();
        if (properties?.CloneNode(deep: true) is { } clonedProperties)
        {
            paragraph.Append(clonedProperties);
        }

        paragraph.Append(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text(PendingText)),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
        return paragraph;
    }

    private static bool IsFieldStart(Paragraph paragraph) => paragraph.Descendants<FieldCode>().Any(IsTableOfContentsCode);

    private static bool IsTableOfContentsCode(FieldCode code) =>
        code.Text.TrimStart().StartsWith(TableOfContentsInstruction + " ", StringComparison.Ordinal);

    private static InvalidOperationException TemplateChanged(string what) =>
        new($"The report Word template has {what}. The template changed; update {nameof(ReportTableOfContents)} to match it.");
}

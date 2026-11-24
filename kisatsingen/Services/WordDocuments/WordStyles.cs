using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The templates' style ids. They are the Norwegian ids Word gave the styles
// when the templates were made, and must match the templates exactly.
internal static class WordStyles
{
    public const string Kicker = "Stikktittel";
    public const string Heading1 = "Overskrift1";
    public const string Heading2 = "Overskrift2";
    public const string Heading3 = "Overskrift3";
    public const string Heading4 = "Overskrift4";
    public const string Heading5 = "Overskrift5";
    public const string Lead = "Ingress";
    public const string ListParagraph = "Listeavsnitt";
    public const string Caption = "Bildetekst";
    public const string FactBox = "Tekstboks";
    public const string Subtitle = "Undertittel";

    // The simple template's only table style is a bare grid, so its tables
    // get borders and a header colour of their own; the report's own table
    // style colours them.
    public const string SimpleTable = "Tabellrutenett";
    public const string ReportTable = "1Vann";

    // The numbering style the templates' own bulleted lists use.
    public const string BulletNumbering = "FKListe";

    public static string? For(DocumentBlockKind kind) => kind switch
    {
        DocumentBlockKind.Kicker => Kicker,
        DocumentBlockKind.Heading1 => Heading1,
        DocumentBlockKind.Heading2 => Heading2,
        DocumentBlockKind.Heading3 => Heading3,
        DocumentBlockKind.Heading4 => Heading4,
        DocumentBlockKind.Heading5 => Heading5,
        DocumentBlockKind.Lead => Lead,
        DocumentBlockKind.Caption => Caption,
        DocumentBlockKind.FactBox => FactBox,
        DocumentBlockKind.BulletList or DocumentBlockKind.NumberedList => ListParagraph,
        _ => null
    };

    public static WordTableFormat TableFormatFor(WordDocumentTemplate template) => template switch
    {
        WordDocumentTemplate.Simple => new WordTableFormat(SimpleTable, IsStyledByTemplate: false),
        WordDocumentTemplate.Report => new WordTableFormat(ReportTable, IsStyledByTemplate: true),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "A template with no table style; add it to WordStyles.")
    };

    // Checked on every build rather than trusted: a missing style id is not an
    // error to Word, it silently falls back to Normal.
    public static void EnsurePresent(MainDocumentPart mainPart, WordDocumentTemplate template)
    {
        var styleIds = mainPart.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Select(style => style.StyleId?.Value)
            .OfType<string>()
            .ToHashSet() ?? [];

        string[] required =
        [
            .. Enum.GetValues<DocumentBlockKind>()
                .Where(kind => DocumentBlockFields.IsAvailableIn(kind, template))
                .Select(For)
                .OfType<string>(),
            TableFormatFor(template).StyleId,
            BulletNumbering,
            .. template == WordDocumentTemplate.Report ? [Subtitle] : Array.Empty<string>()
        ];

        var missing = required.Distinct().Where(id => !styleIds.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The {template} Word template has no style with id {string.Join(", ", missing)}. The template changed; update {nameof(WordStyles)} to match it.");
        }
    }
}

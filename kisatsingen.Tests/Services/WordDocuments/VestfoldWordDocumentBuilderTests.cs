using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.WordDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.WordDocuments;

public sealed class VestfoldWordDocumentBuilderTests
{
    private static readonly DateTimeOffset BuildTime = new(2026, 11, 23, 10, 30, 0, TimeSpan.Zero);
    private static readonly WordDocumentSpec Memo = new("Skolebruksplan 2027", WordDocumentTemplate.Simple,
    [
        new(DocumentBlockKind.Kicker, Text: "23.11.2026"),
        new(DocumentBlockKind.Heading1, Text: "Skolebruksplan 2027"),
        new(DocumentBlockKind.Lead, Text: "Et kort sammendrag av planen."),
        new(DocumentBlockKind.Heading2, Text: "Bakgrunn"),
        new(DocumentBlockKind.Paragraph, Text: "Første linje.\nAndre linje."),
        new(DocumentBlockKind.BulletList, Items: ["Flere elever", "Nye bygg"]),
        new(DocumentBlockKind.Heading3, Text: "Tiltak"),
        new(DocumentBlockKind.NumberedList, Items: ["Kartlegge", "Prioritere"]),
        new(DocumentBlockKind.NumberedList, Items: ["Vedta", "Bygge"]),
        new(DocumentBlockKind.Table, HeaderRow: ["Skole", "Elever", "Bygd"], Rows: [["Thor Heyerdahl", "1200", "1999"], ["Færder", "800", ""]]),
        new(DocumentBlockKind.Caption, Text: "Tabell 1: Elevtall"),
        new(DocumentBlockKind.PageBreak),
        new(DocumentBlockKind.Table, Rows: [["a", "b"]]),
        new(DocumentBlockKind.Table, Rows: [["c", "d"]])
    ]);

    [Fact]
    public void The_built_file_is_a_document_rather_than_a_template()
    {
        using var document = Open(Build(Memo));

        Assert.Equal(WordprocessingDocumentType.Document, document.DocumentType);
    }

    [Fact]
    public void The_file_is_titled_and_dated_when_it_was_built()
    {
        using var document = Open(Build(Memo));

        Assert.Equal(
            ("Skolebruksplan 2027", BuildTime.UtcDateTime, BuildTime.UtcDateTime),
            (document.PackageProperties.Title, document.PackageProperties.Created?.ToUniversalTime(), document.PackageProperties.Modified?.ToUniversalTime()));
    }

    [Fact]
    public void The_templates_own_properties_and_sharepoint_metadata_are_not_carried_into_the_file()
    {
        using var document = Open(Build(Memo));

        Assert.Equal(
            (null, null, null, 0),
            ((object?)document.ExtendedFilePropertiesPart, (object?)document.ThumbnailPart, (object?)document.CustomFilePropertiesPart, document.MainDocumentPart!.CustomXmlParts.Count()));
    }

    [Fact]
    public void The_file_does_not_point_back_at_the_template()
    {
        using var document = Open(Build(Memo));

        Assert.Null(document.MainDocumentPart!.DocumentSettingsPart?.Settings?.GetFirstChild<AttachedTemplate>());
    }

    [Fact]
    public void The_file_has_no_schema_errors()
    {
        using var document = Open(Build(Memo));
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);

        var errors = validator.Validate(document).Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");

        Assert.Empty(errors);
    }

    [Fact]
    public void Only_the_built_content_remains_in_the_body()
    {
        using var document = Open(Build(Memo));

        var texts = Body(document).Descendants<Text>().Select(text => text.Text).Take(3);

        Assert.Equal(["23.11.2026", "Skolebruksplan 2027", "Et kort sammendrag av planen."], texts);
    }

    [Fact]
    public void The_template_content_controls_are_gone()
    {
        using var document = Open(Build(Memo));

        Assert.Empty(Body(document).Descendants<SdtElement>());
    }

    [Fact]
    public void The_section_with_the_logo_and_page_numbers_is_kept_last()
    {
        using var document = Open(Build(Memo));

        var section = Assert.IsType<SectionProperties>(Body(document).LastChild);
        Assert.NotEmpty(section.Elements<HeaderReference>());
    }

    [Fact]
    public void Each_text_block_takes_its_kinds_template_style()
    {
        using var document = Open(Build(Memo));

        var styles = Body(document).Elements<Paragraph>().Take(4).Select(StyleOf);

        Assert.Equal([WordStyles.Kicker, WordStyles.Heading1, WordStyles.Lead, WordStyles.Heading2], styles);
    }

    [Fact]
    public void A_line_break_in_the_text_stays_within_its_paragraph()
    {
        using var document = Open(Build(Memo));

        var paragraph = Body(document).Elements<Paragraph>().Single(p => p.InnerText.StartsWith("Første", StringComparison.Ordinal));

        Assert.Equal(("Første linje.Andre linje.", 1), (paragraph.InnerText, paragraph.Descendants<Break>().Count()));
    }

    [Fact]
    public void Bulleted_items_use_the_templates_own_list()
    {
        using var document = Open(Build(Memo));

        var bulletIds = ListItems(document).Where(item => item.Text is "Flere elever" or "Nye bygg").Select(item => item.NumberingId).Distinct();

        Assert.Equal([TemplateBulletId(document)], bulletIds);
    }

    [Fact]
    public void Each_numbered_list_starts_its_own_count()
    {
        using var document = Open(Build(Memo));

        var items = ListItems(document).ToDictionary(item => item.Text, item => item.NumberingId);

        Assert.Equal((items["Kartlegge"], true), (items["Prioritere"], items["Kartlegge"] != items["Vedta"]));
    }

    [Fact]
    public void Numbered_lists_count_in_decimals_from_one()
    {
        using var document = Open(Build(Memo));
        var numberingId = ListItems(document).First(item => item.Text == "Kartlegge").NumberingId;

        var numbering = document.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        var instance = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == numberingId);
        var level = numbering.Elements<AbstractNum>().Single(a => a.AbstractNumberId?.Value == instance.AbstractNumId!.Val!.Value).Elements<Level>().Single();

        Assert.Equal(
            (NumberFormatValues.Decimal, "%1.", 1),
            (level.NumberingFormat!.Val!.Value, level.LevelText!.Val!.Value, instance.GetFirstChild<LevelOverride>()!.StartOverrideNumberingValue!.Val!.Value));
    }

    [Fact]
    public void A_table_has_its_header_row_repeated_and_every_cell_in_place()
    {
        using var document = Open(Build(Memo));

        var table = Body(document).Elements<Table>().First();
        var rows = table.Elements<TableRow>().ToList();

        Assert.Equal(
            (3, 3, true, "Færder"),
            (table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count(), rows.Count, rows[0].TableRowProperties?.GetFirstChild<TableHeader>() is not null, rows[2].Elements<TableCell>().First().InnerText));
    }

    [Fact]
    public void Two_tables_in_a_row_are_kept_apart_by_a_paragraph()
    {
        using var document = Open(Build(Memo));

        var lastTables = Body(document).Elements<Table>().Skip(1).ToList();

        Assert.IsType<Paragraph>(lastTables[0].NextSibling());
    }

    [Fact]
    public void A_table_at_the_end_is_followed_by_a_paragraph_before_the_section()
    {
        using var document = Open(Build(Memo));

        var section = Body(document).LastChild!;

        Assert.IsType<Paragraph>(section.PreviousSibling());
    }

    [Fact]
    public void A_page_break_block_breaks_the_page()
    {
        using var document = Open(Build(Memo));

        Assert.Single(Body(document).Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
    }

    private static byte[] Build(WordDocumentSpec spec) =>
        new VestfoldWordDocumentBuilder(new WordTemplateFiles(), new FixedTimeProvider(BuildTime)).Build(spec).ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

    private static Body Body(WordprocessingDocument document) => document.MainDocumentPart!.Document!.Body!;

    private static string? StyleOf(Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    private static IEnumerable<(string Text, int? NumberingId)> ListItems(WordprocessingDocument document) =>
        Body(document).Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .Select(paragraph => (paragraph.InnerText, paragraph.ParagraphProperties!.NumberingProperties!.NumberingId?.Val?.Value));

    private static int? TemplateBulletId(WordprocessingDocument document) =>
        document.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Single(style => style.StyleId?.Value == WordStyles.BulletNumbering)
            .StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
}

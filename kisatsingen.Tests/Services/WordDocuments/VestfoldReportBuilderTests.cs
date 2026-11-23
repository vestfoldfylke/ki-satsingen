using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.WordDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.WordDocuments;

public sealed class VestfoldReportBuilderTests
{
    private static readonly WordDocumentSpec Report = new("Årsrapport 2026", WordDocumentTemplate.Report,
    [
        new(DocumentBlockKind.Heading2, Text: "Innledning"),
        new(DocumentBlockKind.Lead, Text: "Et år med vekst."),
        new(DocumentBlockKind.Paragraph, Text: "Brødtekst."),
        new(DocumentBlockKind.Heading3, Text: "Økonomi"),
        new(DocumentBlockKind.Heading4, Text: "Drift"),
        new(DocumentBlockKind.FactBox, Text: "Driften gikk i balanse."),
        new(DocumentBlockKind.BulletList, Items: ["Skole", "Vei"]),
        new(DocumentBlockKind.Table, HeaderRow: ["Område", "Kroner"], Rows: [["Skole", "100"]])
    ],
    Subtitle: "Vestfold fylkeskommune",
    Date: "23.11.2026",
    CoverText: "Utarbeidet av økonomiavdelingen.");

    private static readonly WordDocumentSpec BareReport = Report with { Subtitle = null, Date = null, CoverText = null };

    [Fact]
    public void The_built_file_is_a_document_rather_than_a_template()
    {
        using var document = Open(Build(Report));

        Assert.Equal(WordprocessingDocumentType.Document, document.DocumentType);
    }

    // The glossary, the template's gallery of alternative covers, is left out:
    // it is the template's own, untouched by the builder, and has errors as
    // the template ships.
    [Fact]
    public void The_parts_the_builder_writes_have_no_schema_errors()
    {
        using var document = Open(Build(Report));
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);

        var errors = validator.Validate(document)
            .Where(error => error.Part is not GlossaryDocumentPart)
            .Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");

        Assert.Empty(errors);
    }

    [Fact]
    public void The_cover_and_the_footer_show_the_title()
    {
        using var document = Open(Build(Report));

        Assert.Equal(
            (true, true),
            (BodyText(document).Contains("Årsrapport 2026", StringComparison.Ordinal), document.MainDocumentPart!.FooterParts.Any(footer => footer.Footer!.InnerText.Contains("Årsrapport 2026", StringComparison.Ordinal))));
    }

    [Fact]
    public void The_cover_shows_the_subtitle_date_and_cover_text_it_was_given()
    {
        using var document = Open(Build(Report));
        var text = BodyText(document);

        Assert.All(["Vestfold fylkeskommune", "23.11.2026", "Utarbeidet av økonomiavdelingen."], given => Assert.Contains(given, text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Veiledning")]
    [InlineData("Skriv en tittel")]
    [InlineData("Kort undertittel")]
    [InlineData("Velg dato")]
    [InlineData("Tekstfelt")]
    [InlineData("Her kan du skrive")]
    [InlineData("Navn Navnesen")]
    [InlineData("Kolonnetittel")]
    [InlineData("Skriv inn overskrift")]
    [InlineData("faktaboks")]
    public void No_template_guidance_or_placeholder_text_is_left(string templateText)
    {
        using var document = Open(Build(BareReport));

        Assert.DoesNotContain(templateText, BodyText(document), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cover_without_a_subtitle_has_no_subtitle_paragraph()
    {
        using var document = Open(Build(BareReport));

        Assert.DoesNotContain(Body(document).Descendants<ParagraphStyleId>(), style => style.Val?.Value == WordStyles.Subtitle);
    }

    [Fact]
    public void A_cover_without_cover_text_has_no_text_box()
    {
        using var document = Open(Build(BareReport));

        Assert.Equal((0, 0), (TextBoxShapes(document).Count(), CoverParagraphsStyled(document, "forside--tekstboks").Count()));
    }

    [Fact]
    public void A_cover_with_cover_text_has_the_text_box_without_its_placeholder_title()
    {
        using var document = Open(Build(Report));

        Assert.Equal(
            (2, 0, "Utarbeidet av økonomiavdelingen.|Utarbeidet av økonomiavdelingen."),
            (TextBoxShapes(document).Count(), CoverParagraphsStyled(document, "forside--tekstbokstittel").Count(), string.Join('|', CoverParagraphsStyled(document, "forside--tekstboks").Select(paragraph => paragraph.InnerText))));
    }

    [Fact]
    public void The_table_of_contents_waits_for_word_to_update_it_when_the_file_opens()
    {
        using var document = Open(Build(Report));
        var settings = document.MainDocumentPart!.DocumentSettingsPart!.Settings!;

        var tocBegin = Body(document).Descendants<FieldCode>().Single(code => code.Text.TrimStart().StartsWith("TOC", StringComparison.Ordinal))
            .Parent!.PreviousSibling<Run>()!.GetFirstChild<FieldChar>()!;

        Assert.Equal(
            (true, true, true),
            (settings.GetFirstChild<UpdateFieldsOnOpen>()?.Val?.Value, tocBegin.Dirty?.Value, BodyText(document).Contains(ReportTableOfContents.PendingText, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_content_comes_after_the_table_of_contents_and_before_the_back_page()
    {
        using var document = Open(Build(Report));
        var children = Body(document).ChildElements.ToList();

        var sectionBreak = children.FindIndex(child => child is Paragraph { ParagraphProperties.SectionProperties: not null });
        var firstHeading = children.FindIndex(child => child.InnerText == "Innledning");
        var backPage = children.FindIndex(child => child is SdtBlock && child.InnerText.Contains("Postboks 1213", StringComparison.Ordinal));

        Assert.True(sectionBreak < firstHeading && firstHeading < backPage, $"break {sectionBreak}, heading {firstHeading}, back page {backPage}");
    }

    [Fact]
    public void The_report_blocks_take_the_reports_styles()
    {
        using var document = Open(Build(Report));

        var styles = Body(document).Elements<Paragraph>()
            .Where(paragraph => paragraph.InnerText is "Driften gikk i balanse." or "Drift")
            .Select(paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);

        Assert.Equal([WordStyles.Heading4, WordStyles.FactBox], styles);
    }

    [Fact]
    public void Tables_take_the_reports_table_style_without_borders_of_their_own()
    {
        using var document = Open(Build(Report));

        var properties = Body(document).Elements<Table>().Single().GetFirstChild<TableProperties>()!;

        Assert.Equal((WordStyles.ReportTable, null), (properties.TableStyle?.Val?.Value, properties.TableBorders));
    }

    private static readonly DateTimeOffset BuildTime = new(2026, 11, 23, 10, 30, 0, TimeSpan.Zero);

    private static byte[] Build(WordDocumentSpec spec) =>
        new VestfoldWordDocumentBuilder(new WordTemplateFiles(), new FixedTimeProvider(BuildTime)).Build(spec).ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

    private static Body Body(WordprocessingDocument document) => document.MainDocumentPart!.Document!.Body!;

    // Word stores the cover's drawing twice: once for itself and once for older readers.
    private static IEnumerable<OpenXmlElement> TextBoxShapes(WordprocessingDocument document) =>
        Body(document).Descendants<DocumentFormat.OpenXml.Office2010.Word.DrawingShape.WordprocessingShape>()
            .Where(shape => shape.GetFirstChild<DocumentFormat.OpenXml.Office2010.Word.DrawingShape.NonVisualDrawingProperties>()?.Name?.Value == "Tekstboks")
            .Concat<OpenXmlElement>(Body(document).Descendants<DocumentFormat.OpenXml.Vml.Shape>().Where(shape => shape.Id?.Value == "Tekstboks"));

    private static IEnumerable<Paragraph> CoverParagraphsStyled(WordprocessingDocument document, string styleId) =>
        Body(document).Descendants<Paragraph>().Where(paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == styleId);

    private static string BodyText(WordprocessingDocument document) => string.Join('\n', Body(document).Descendants<Text>().Select(text => text.Text));
}

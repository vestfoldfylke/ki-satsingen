using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.MeetingDocuments;
using kisatsingen.Services.WordDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.MeetingDocuments;

public sealed class VestfoldMeetingDocumentBuilderTests
{
    private static readonly MeetingDocumentSpec Minutes = new(
        "Referat styremøte",
        MeetingDocumentKind.Minutes,
        "Styremøte",
        [
            new("1", "Godkjenning av innkalling", Decision: "Godkjent"),
            new("2", "Budsjett 2027", Decision: "Vedtatt", Responsible: "Kari", Deadline: "01.12.2026")
        ],
        Date: "24.11.2026",
        Time: "10.00–11.30",
        Place: "Fylkeshuset",
        Invited: "Kari, Ola",
        Absent: "Per",
        MinuteTaker: "Ola");

    [Fact]
    public void The_file_has_no_schema_errors()
    {
        using var document = Open(Build(Minutes));
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);

        var errors = validator.Validate(document).Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(MeetingDocumentKind.Minutes, "Referat")]
    [InlineData(MeetingDocumentKind.Notice, "Møteinnkalling")]
    public void The_heading_follows_the_kind(MeetingDocumentKind kind, string expected)
    {
        using var document = Open(Build(Minutes with { Kind = kind }));

        Assert.Equal(expected, Body(document).Elements<Paragraph>().First().InnerText);
    }

    [Fact]
    public void Each_field_holds_its_value_after_its_label()
    {
        using var document = Open(Build(Minutes));

        var lines = Body(document).Elements<Paragraph>().Skip(1).Take(4).Select(paragraph => paragraph.InnerText);

        Assert.Equal(["MøteStyremøte", "Dato24.11.2026Tid10.00–11.30StedFylkeshuset", "InnkaltKari, Ola", "ForfallPerReferentOla"], lines);
    }

    [Fact]
    public void Fields_left_out_stay_blank_without_placeholder_text()
    {
        using var document = Open(Build(Minutes with { Date = null, Absent = null }));

        var text = Body(document).InnerText;

        Assert.Equal((false, false, false), (text.Contains('[', StringComparison.Ordinal), text.Contains("Per", StringComparison.Ordinal), Body(document).Descendants<SdtRun>().Any()));
    }

    [Fact]
    public void The_agenda_table_has_the_header_row_and_one_row_per_item()
    {
        using var document = Open(Build(Minutes));

        var rows = Body(document).Elements<Table>().Single().Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Select(cell => cell.InnerText).ToArray())
            .ToList();

        Assert.Equal(
            [
                ["Sak", "Beskrivelse", "Beslutning", "Ansvarlig", "Frist"],
                ["1", "Godkjenning av innkalling", "Godkjent", "", ""],
                ["2", "Budsjett 2027", "Vedtatt", "Kari", "01.12.2026"]
            ],
            rows);
    }

    [Fact]
    public void The_page_stays_landscape()
    {
        using var document = Open(Build(Minutes));

        Assert.Equal(PageOrientationValues.Landscape, Body(document).GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!.Orient!.Value);
    }

    [Fact]
    public void The_file_is_a_titled_document_rather_than_a_template()
    {
        using var document = Open(Build(Minutes));

        Assert.Equal((WordprocessingDocumentType.Document, "Referat styremøte"), (document.DocumentType, document.PackageProperties.Title));
    }

    private static byte[] Build(MeetingDocumentSpec spec) =>
        new VestfoldMeetingDocumentBuilder(new WordTemplateFiles(), TimeProvider.System).Build(spec).ToArray();

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

    private static Body Body(WordprocessingDocument document) => document.MainDocumentPart!.Document!.Body!;
}

using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

// How the layout of a page becomes Markdown: headings, paragraphs, lists,
// table rows and words split across lines.
public sealed class VestfoldPdfConverterTests
{
    private const double TopY = 720;

    // Single line spacing for the 12 pt body text: 1.2 x the font size.
    private const double SingleSpacing = TestPdf.BodyFontSize * 1.2;

    private static async Task<string> ConvertToMarkdownAsync(byte[] pdf)
    {
        var result = await VestfoldPdfConverter.ConvertAsync(new MemoryStream(pdf), int.MaxValue);

        Assert.True(result.Success, $"The test PDF should convert, but failed with: {result.ErrorMessage}");
        return result.Markdown;
    }

    private static string Paragraphs(params string[] paragraphs) =>
        string.Join(Environment.NewLine + Environment.NewLine, paragraphs);

    // Body text lines one after another, starting at y and spaced by spacing.
    private static IEnumerable<PlacedLine> BodyLines(double y, double spacing, params string[] texts) =>
        texts.Select((text, index) => new PlacedLine(text, y - index * spacing));

    [Fact]
    public async Task Lines_of_one_paragraph_are_joined_into_one_line()
    {
        var pdf = TestPdf.Page([.. BodyLines(TopY, SingleSpacing, "Linje en", "linje to", "linje tre")]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal("Linje en linje to linje tre", markdown);
    }

    [Fact]
    public async Task A_gap_larger_than_the_line_spacing_starts_a_new_paragraph()
    {
        var pdf = TestPdf.Page(
        [
            .. BodyLines(TopY, SingleSpacing, "Første avsnitt", "fortsetter her."),
            .. BodyLines(TopY - SingleSpacing * 3, SingleSpacing, "Andre avsnitt", "fortsetter her.")
        ]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("Første avsnitt fortsetter her.", "Andre avsnitt fortsetter her."), markdown);
    }

    // At 1.6 x the font size each step is over the paragraph limit for single
    // spacing, so this only holds when the limit follows the page's own spacing.
    [Fact]
    public async Task Lines_in_a_document_with_wide_line_spacing_stay_one_paragraph()
    {
        var pdf = TestPdf.Page([.. BodyLines(TopY, TestPdf.BodyFontSize * 1.6, "Linje en", "linje to", "linje tre")]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal("Linje en linje to linje tre", markdown);
    }

    [Fact]
    public async Task A_line_in_a_larger_font_becomes_a_heading()
    {
        var pdf = TestPdf.Page(
        [
            new PlacedLine("Budsjett", TopY + 30, FontSize: 18),
            .. BodyLines(TopY, SingleSpacing, "Tallene for neste periode", "er klare.")
        ]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("# Budsjett", "Tallene for neste periode er klare."), markdown);
    }

    [Fact]
    public async Task Heading_levels_follow_the_font_sizes_from_largest_to_smallest()
    {
        var pdf = TestPdf.Page(
        [
            new PlacedLine("Hoveddel", TopY + 70, FontSize: 24),
            new PlacedLine("Underdel", TopY + 35, FontSize: 18),
            new PlacedLine("Delpunkt", TopY + 15, FontSize: 14),
            .. BodyLines(TopY, SingleSpacing, "Brødtekst som er lengre", "enn alle overskriftene.")
        ]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(
            Paragraphs("# Hoveddel", "## Underdel", "### Delpunkt", "Brødtekst som er lengre enn alle overskriftene."),
            markdown);
    }

    [Fact]
    public async Task A_bold_line_slightly_larger_than_the_body_text_becomes_a_heading()
    {
        var pdf = TestPdf.Page(
        [
            new PlacedLine("Merknad", TopY + SingleSpacing, FontSize: 13, IsBold: true),
            .. BodyLines(TopY, SingleSpacing, "Teksten under merknaden", "fyller to linjer.")
        ]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("# Merknad", "Teksten under merknaden fyller to linjer."), markdown);
    }

    // Bold alone is emphasis, not a heading, but it still is not part of the
    // paragraph below it.
    [Fact]
    public async Task A_bold_line_the_size_of_the_body_text_is_its_own_paragraph_but_no_heading()
    {
        var pdf = TestPdf.Page(
        [
            new PlacedLine("Merknad", TopY + SingleSpacing, IsBold: true),
            .. BodyLines(TopY, SingleSpacing, "Teksten under merknaden", "fyller to linjer.")
        ]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("Merknad", "Teksten under merknaden fyller to linjer."), markdown);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("1.")]
    [InlineData("1)")]
    [InlineData("a)")]
    public async Task Each_list_item_is_its_own_paragraph(string marker)
    {
        var pdf = TestPdf.Page([.. BodyLines(TopY, SingleSpacing, "Tiltakene er:", $"{marker} Første tiltak", $"{marker} Andre tiltak")]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("Tiltakene er:", $"{marker} Første tiltak", $"{marker} Andre tiltak"), markdown);
    }

    // A wide gap between the words of a line means columns, so rows are not
    // run together into one sentence.
    [Fact]
    public async Task Each_table_row_is_its_own_paragraph()
    {
        const double secondColumnX = 300;
        var pdf = TestPdf.Page(
            new PlacedLine("Post", TopY),
            new PlacedLine("Sum", TopY, X: secondColumnX),
            new PlacedLine("Drift", TopY - SingleSpacing),
            new PlacedLine("100", TopY - SingleSpacing, X: secondColumnX),
            new PlacedLine("Investering", TopY - SingleSpacing * 2),
            new PlacedLine("250", TopY - SingleSpacing * 2, X: secondColumnX));

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(Paragraphs("Post Sum", "Drift 100", "Investering 250"), markdown);
    }

    [Theory]
    [InlineData("Saken gjelder avde-", "lingen for skole.", "Saken gjelder avdelingen for skole.")]
    [InlineData("Saken gjelder IT-", "avdelingen i fylket.", "Saken gjelder IT-avdelingen i fylket.")]
    [InlineData("Saken gjelder kultur-", "og idrettsfeltet.", "Saken gjelder kultur- og idrettsfeltet.")]
    [InlineData("Saken gjelder 2027-", "2030.", "Saken gjelder 2027- 2030.")]
    public async Task A_word_split_across_lines_is_joined_as_it_was_written(string firstLine, string secondLine, string expected)
    {
        var pdf = TestPdf.Page([.. BodyLines(TopY, SingleSpacing, firstLine, secondLine)]);

        var markdown = await ConvertToMarkdownAsync(pdf);

        Assert.Equal(expected, markdown);
    }
}

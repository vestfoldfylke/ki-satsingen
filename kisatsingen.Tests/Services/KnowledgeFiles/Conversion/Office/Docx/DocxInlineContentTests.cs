using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// What a paragraph holds besides plain text: raised and lowered text, images,
// symbols, and the spacing between them.
public sealed class DocxInlineContentTests
{
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static string Raised(string text) => Run(text, "<w:vertAlign w:val=\"superscript\"/>");

    private static string Image(string docPropertiesAttributes) =>
        $"<w:r><w:drawing><wp:inline xmlns:wp=\"{DrawingNamespace}\"><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"1\" name=\"Bilde 1\" {docPropertiesAttributes}/></wp:inline></w:drawing></w:r>";

    // x2 and H2O mean something else than x² and H₂O.
    [Theory]
    [InlineData("superscript", "x<sup>2</sup>")]
    [InlineData("subscript", "x<sub>2</sub>")]
    public void Raised_and_lowered_text_keeps_its_position(string position, string expected)
    {
        var docx = WithBody($"<w:p>{Run("x")}{Run("2", $"<w:vertAlign w:val=\"{position}\"/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void Raised_text_split_across_runs_is_raised_as_one()
    {
        var docx = WithBody($"<w:p>{Run("1")}{Raised("s")}{Raised("t")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("1<sup>st</sup>", markdown);
    }

    [Fact]
    public void A_character_style_can_raise_text()
    {
        var body = $"<w:p>{Run("x")}<w:r><w:rPr><w:rStyle w:val=\"Hevet\"/></w:rPr><w:t>2</w:t></w:r></w:p>";
        var docx = WithParts(body, styles: CharacterStyle("Hevet", "Hevet", "<w:vertAlign w:val=\"superscript\"/>"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("x<sup>2</sup>", markdown);
    }

    [Fact]
    public void A_run_set_to_the_baseline_is_not_raised_by_its_style()
    {
        var body = $"<w:p>{Run("x")}<w:r><w:rPr><w:rStyle w:val=\"Hevet\"/><w:vertAlign w:val=\"baseline\"/></w:rPr><w:t>2</w:t></w:r></w:p>";
        var docx = WithParts(body, styles: CharacterStyle("Hevet", "Hevet", "<w:vertAlign w:val=\"superscript\"/>"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("x2", markdown);
    }

    // Word raises every footnote reference; [^1] is already a reference.
    [Fact]
    public void A_raised_footnote_reference_is_not_put_in_sup()
    {
        var body = $"<w:p>{Run("Tekst")}<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r></w:p>";
        var docx = WithParts(body, footnotes: $"<w:footnote w:id=\"1\">{Paragraph("Kilde")}</w:footnote>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst[^1]\n\n[^1]: Kilde", markdown);
    }

    [Fact]
    public void An_image_is_written_with_its_description()
    {
        var docx = WithBody($"<w:p>{Run("Se ")}{Image("descr=\"Kart over Vestfold\" title=\"Kart\"")}{Run(" her.")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Se ![Kart over Vestfold]() her.", markdown);
    }

    [Fact]
    public void An_image_without_a_description_is_written_with_its_title()
    {
        var docx = WithBody($"<w:p>{Image("title=\"Kart\"")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("![Kart]()", markdown);
    }

    [Fact]
    public void An_image_without_description_or_title_is_left_out()
    {
        var docx = WithBody($"<w:p>{Run("Før ")}{Image("")}{Run(" etter")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Før etter", markdown);
    }

    [Fact]
    public void Brackets_in_an_image_description_are_escaped()
    {
        var docx = WithBody($"<w:p>{Image("descr=\"Figur [1]\"")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("![Figur \\[1\\]]()", markdown);
    }

    [Fact]
    public void A_symbol_with_a_unicode_character_is_kept()
    {
        var docx = WithBody($"<w:p>{Run("Ferdig ")}<w:r><w:sym w:font=\"Segoe UI Symbol\" w:char=\"2713\"/></w:r></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Ferdig ✓", markdown);
    }

    // F04A is a smiley in Wingdings and a J in any other font.
    [Fact]
    public void A_symbol_font_s_own_character_is_left_out()
    {
        var docx = WithBody($"<w:p>{Run("Hei ")}<w:r><w:sym w:font=\"Wingdings\" w:char=\"F04A\"/></w:r>{Run(" du")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Hei du", markdown);
    }

    [Theory]
    [InlineData("a   b")]
    [InlineData("a\tb")]
    public void Spaces_and_tabs_in_a_row_are_one_space(string text)
    {
        var docx = WithParagraphs(text);

        var markdown = ToMarkdown(docx);

        Assert.Equal("a b", markdown);
    }

    // A space before a formatted run and one inside it, as Word often writes them.
    [Fact]
    public void Spaces_on_both_sides_of_a_marker_are_one_space()
    {
        var docx = WithBody($"<w:p>{Run("og ")}{Run(" fet", "<w:b/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("og **fet**", markdown);
    }

    [Fact]
    public void A_no_break_space_is_kept()
    {
        var docx = WithParagraphs("10 000 kr");

        var markdown = ToMarkdown(docx);

        Assert.Equal("10 000 kr", markdown);
    }
}

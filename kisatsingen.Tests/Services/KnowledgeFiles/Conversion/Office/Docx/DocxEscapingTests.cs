using System.Security;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// The document's own text stays text when it looks like Markdown, and is
// left as it is where Markdown wouldn't read it.
public sealed class DocxEscapingTests
{
    // The text as Word stores it, so < and & are characters, not XML.
    private static byte[] WithText(string text) => WithParagraphs(SecurityElement.Escape(text));

    [Theory]
    [InlineData("a *b* c", "a \\*b\\* c")]
    [InlineData("_understrek_", "\\_understrek\\_")]
    [InlineData("`kode`", "\\`kode\\`")]
    [InlineData("[klammer](ikke-lenke)", "\\[klammer\\](ikke-lenke)")]
    [InlineData("~tilde~", "\\~tilde\\~")]
    [InlineData("koster $5", "koster \\$5")]
    [InlineData("<b>ikke fet</b>", "\\<b>ikke fet\\</b>")]
    [InlineData("&amp; og &#169;", "\\&amp; og \\&#169;")]
    [InlineData("\\* og slutt\\", "\\\\\\* og slutt\\\\")]
    public void Markdown_characters_in_the_text_stay_text(string text, string expected)
    {
        var docx = WithText(text);

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    [Theory]
    [InlineData("snake_case_navn")]
    [InlineData("C:\\Users\\ola")]
    [InlineData("a < b og b > c")]
    [InlineData("AT&T")]
    [InlineData("1 + 2 = 3")]
    [InlineData("1.5 liter")]
    [InlineData("-5 grader")]
    public void Characters_markdown_would_not_read_where_they_are_are_left_as_they_are(string text)
    {
        var docx = WithText(text);

        var markdown = ToMarkdown(docx);

        Assert.Equal(text, markdown);
    }

    [Theory]
    [InlineData("- ikke en liste", "\\- ikke en liste")]
    [InlineData("+ ikke en liste", "\\+ ikke en liste")]
    [InlineData("1. ikke en liste", "1\\. ikke en liste")]
    [InlineData("2) ikke en liste", "2\\) ikke en liste")]
    [InlineData("2026. var et godt år", "2026\\. var et godt år")]
    [InlineData("***", "\\*\\*\\*")]
    public void A_line_that_would_start_a_list_or_a_rule_stays_text(string text, string expected)
    {
        var docx = WithText(text);

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void A_heading_s_text_is_escaped()
    {
        var docx = WithBody($"<w:p><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr>{Run("Overskrift med **fet** i seg")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("## Overskrift med \\*\\*fet\\*\\* i seg", markdown);
    }

    [Fact]
    public void A_table_cell_s_text_is_escaped()
    {
        var docx = WithBody($"<w:tbl><w:tr><w:tc>{Paragraph("*a*")}</w:tc></w:tr></w:tbl>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("| \\*a\\* |\n| --- |", markdown);
    }

    [Fact]
    public void A_bold_run_starting_with_an_escaped_character_after_a_letter_gets_a_space()
    {
        var docx = WithBody($"<w:p>{Run("a")}{Run("*b", "<w:b/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("a **\\*b**", markdown);
    }
}

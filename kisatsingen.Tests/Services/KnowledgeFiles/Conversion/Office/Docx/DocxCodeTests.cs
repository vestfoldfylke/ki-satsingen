using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// Code is recognised by its style's name, as Pandoc writes it and Word names
// its HTML styles: a paragraph style makes a code block, a character style inline code.
public sealed class DocxCodeTests
{
    private static readonly string CodeStyles =
        ParagraphStyle("SourceCode", "Source Code") + CharacterStyle("VerbatimChar", "Verbatim Char", "<w:rFonts w:ascii=\"Consolas\"/>");

    private static string CodeParagraph(string text) => StyledParagraph("SourceCode", text);

    private static string InlineCode(string text) => $"<w:r><w:rPr><w:rStyle w:val=\"VerbatimChar\"/></w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static byte[] WithCode(string body) => WithParts(body, styles: CodeStyles);

    [Fact]
    public void Code_paragraphs_in_a_row_become_one_fenced_code_block()
    {
        var docx = WithCode(CodeParagraph("function hilsen() {") + CodeParagraph("  return 1;") + CodeParagraph("}"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\nfunction hilsen() {\n  return 1;\n}\n```", markdown);
    }

    [Fact]
    public void Code_keeps_its_markdown_characters_unescaped()
    {
        var docx = WithCode(CodeParagraph("// *stjerner* og _understrek_ og # hash"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\n// *stjerner* og _understrek_ og # hash\n```", markdown);
    }

    [Fact]
    public void An_empty_line_inside_code_is_kept_and_those_around_it_are_not()
    {
        var docx = WithCode(CodeParagraph("") + CodeParagraph("a") + CodeParagraph("") + CodeParagraph("b") + CodeParagraph(""));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\na\n\nb\n```", markdown);
    }

    [Fact]
    public void A_code_line_of_only_spaces_at_the_block_s_edge_is_left_out()
    {
        var docx = WithCode(CodeParagraph("   ") + CodeParagraph("a"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\na\n```", markdown);
    }

    [Fact]
    public void Code_paragraphs_without_text_write_no_block()
    {
        var docx = WithCode(CodeParagraph("") + Paragraph("Tekst"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst", markdown);
    }

    [Fact]
    public void A_tab_in_code_is_kept()
    {
        var docx = WithCode($"<w:p><w:pPr><w:pStyle w:val=\"SourceCode\"/></w:pPr><w:r><w:tab/></w:r>{Run("innrykket")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\n\tinnrykket\n```", markdown);
    }

    [Fact]
    public void A_code_block_ends_at_the_next_paragraph_that_is_not_code()
    {
        var docx = WithCode(CodeParagraph("a") + Paragraph("Tekst") + CodeParagraph("b"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\na\n```\n\nTekst\n\n```\nb\n```", markdown);
    }

    [Fact]
    public void A_code_block_before_a_table_is_written_before_it()
    {
        var docx = WithCode(CodeParagraph("a") + $"<w:tbl><w:tr><w:tc>{Paragraph("Celle")}</w:tc></w:tr></w:tbl>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\na\n```\n\n| Celle |\n| --- |", markdown);
    }

    [Fact]
    public void A_code_block_s_fence_is_longer_than_the_backticks_in_it()
    {
        var docx = WithCode(CodeParagraph("```"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("````\n```\n````", markdown);
    }

    [Theory]
    [InlineData("Source Code")]
    [InlineData("HTML Preformatted")]
    public void A_paragraph_style_is_code_by_its_name(string name)
    {
        var docx = WithParts(StyledParagraph("Kode", "x = 1"), styles: ParagraphStyle("Kode", name));

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\nx = 1\n```", markdown);
    }

    [Fact]
    public void A_style_based_on_a_code_style_is_code()
    {
        var styles = ParagraphStyle("SourceCode", "Source Code") + ParagraphStyle("MinKode", "Min kode", basedOn: "SourceCode");
        var docx = WithParts(StyledParagraph("MinKode", "x = 1"), styles: styles);

        var markdown = ToMarkdown(docx);

        Assert.Equal("```\nx = 1\n```", markdown);
    }

    [Fact]
    public void Text_in_a_code_character_style_becomes_inline_code()
    {
        var docx = WithCode($"<w:p>{Run("Kjør ")}{InlineCode("npm install")}{Run(" nå.")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Kjør `npm install` nå.", markdown);
    }

    [Theory]
    [InlineData("Verbatim Char")]
    [InlineData("HTML Code")]
    public void A_character_style_is_code_by_its_name(string name)
    {
        var docx = WithParts($"<w:p><w:r><w:rPr><w:rStyle w:val=\"Kode\"/></w:rPr><w:t>x</w:t></w:r></w:p>", styles: CharacterStyle("Kode", name, ""));

        var markdown = ToMarkdown(docx);

        Assert.Equal("`x`", markdown);
    }

    // Word splits text into runs at every edit.
    [Fact]
    public void Inline_code_split_across_runs_is_one_code_span()
    {
        var docx = WithCode($"<w:p>{InlineCode("con")}{InlineCode("sole")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("`console`", markdown);
    }

    [Fact]
    public void Inline_code_is_not_escaped()
    {
        var docx = WithCode($"<w:p>{InlineCode("*a* [b]")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("`*a* [b]`", markdown);
    }

    [Fact]
    public void Inline_code_with_backticks_in_it_gets_a_longer_fence()
    {
        var docx = WithCode($"<w:p>{InlineCode("git commit -m `melding`")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("`` git commit -m `melding` ``", markdown);
    }
}

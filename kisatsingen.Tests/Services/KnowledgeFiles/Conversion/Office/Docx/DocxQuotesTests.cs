using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxQuotesTests
{
    // As Norwegian Word defines them: italic, with the built-in English names.
    private static readonly string QuoteStyles =
        ParagraphStyle("Sitat", "Quote", "<w:rPr><w:i/></w:rPr>") +
        ParagraphStyle("Sterktsitat", "Intense Quote", "<w:rPr><w:i/></w:rPr>") +
        ParagraphStyle("Overskrift1", "heading 1", "<w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>");

    private static byte[] WithQuoteStyles(string body) => WithParts(body, styles: QuoteStyles);

    [Theory]
    [InlineData("Sitat")]
    [InlineData("Sterktsitat")]
    public void A_quote_style_becomes_a_markdown_quote(string styleId)
    {
        var docx = WithQuoteStyles(StyledParagraph(styleId, "Sitert tekst"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("> *Sitert tekst*", markdown);
    }

    // Word splits a paragraph into several runs at every edit or spell check.
    [Fact]
    public void A_quote_split_into_several_runs_is_quoted_as_a_whole()
    {
        var docx = WithQuoteStyles($"<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>{Run("Sitert ")}{Run("tekst")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("> *Sitert tekst*", markdown);
    }

    [Fact]
    public void Every_line_of_a_quote_is_quoted()
    {
        var docx = WithQuoteStyles($"<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>{Run("Første")}<w:r><w:br/></w:r>{Run("Andre")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("> *Første  \n> Andre*", markdown);
    }

    // Markdown only reads "> " at the start of the line, so the strikethrough goes inside the quote.
    [Fact]
    public void A_struck_quote_is_struck_through_inside_the_quote()
    {
        var docx = WithQuoteStyles($"<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>{Run("Sitert tekst", "<w:strike/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("> *~~Sitert tekst~~*", markdown);
    }

    // Italic is switched off, since a line starting with * can't start a heading anyway.
    [Fact]
    public void A_quote_starting_with_a_hash_stays_text_inside_the_quote()
    {
        var docx = WithQuoteStyles($"<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>{Run("#1 i Norge", "<w:i w:val=\"0\"/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("> \\#1 i Norge", markdown);
    }

    // An empty quote paragraph is common: pressing Enter after a quote keeps the style.
    [Fact]
    public void An_empty_quote_paragraph_is_dropped()
    {
        var docx = WithQuoteStyles(StyledParagraph("Overskrift1", "Innledning") + "<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("# Innledning", markdown);
    }

    // A quote must not depend on anything else in the document.
    [Fact]
    public void A_quote_after_a_heading_is_still_a_quote()
    {
        var docx = WithQuoteStyles(StyledParagraph("Overskrift1", "Innledning") + StyledParagraph("Sitat", "Sitert tekst"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("# Innledning\n\n> *Sitert tekst*", markdown);
    }

    // A text box nested in a quote is a block of its own.
    [Fact]
    public void A_text_box_in_a_quote_is_not_quoted()
    {
        var textBox = $"<w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:textbox><w:txbxContent>{Paragraph("Boks")}</w:txbxContent></v:textbox></v:shape></w:pict></w:r>";
        var docx = WithQuoteStyles($"<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>{textBox}{Run("Sitert tekst")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("> *Sitert tekst*\n\nBoks", markdown);
    }

    [Fact]
    public void A_plain_paragraph_is_no_quote()
    {
        var docx = WithQuoteStyles(Paragraph("Vanlig tekst"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Vanlig tekst", markdown);
    }
}

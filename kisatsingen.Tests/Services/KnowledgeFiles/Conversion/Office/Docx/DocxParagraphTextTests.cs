using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// Which text of a paragraph is written, and how breaks and spacing come out.
public sealed class DocxParagraphTextTests
{
    [Fact]
    public void Paragraphs_are_separated_by_a_blank_line()
    {
        var docx = WithParagraphs("Første avsnitt", "Andre avsnitt");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Første avsnitt\n\nAndre avsnitt", markdown);
    }

    // Word splits words across runs at every edit or spell check.
    [Fact]
    public void Runs_are_joined_without_added_spaces()
    {
        var docx = WithBody($"<w:p>{Run("Hen")}{Run("visning")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Henvisning", markdown);
    }

    [Fact]
    public void Empty_paragraphs_are_dropped()
    {
        var docx = WithBody($"{Paragraph("Før")}<w:p/>{Paragraph("   ")}{Paragraph("Etter")}");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Før\n\nEtter", markdown);
    }

    [Fact]
    public void A_line_break_in_a_paragraph_stays_a_line_break()
    {
        var docx = WithBody($"<w:p>{Run("PP-tjenesten")}<w:r><w:br/></w:r>{Run("Vestfold fylkeskommune")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("PP-tjenesten  \nVestfold fylkeskommune", markdown);
    }

    // Two line breaks would read as a paragraph break and split the bold in two.
    [Fact]
    public void Line_breaks_in_a_row_inside_bold_text_keep_the_bold_whole()
    {
        var docx = WithBody($"<w:p>{Run("Første", "<w:b/>")}<w:r><w:rPr><w:b/></w:rPr><w:br/><w:br/></w:r>{Run("Andre", "<w:b/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("**Første  \nAndre**", markdown);
    }

    [Fact]
    public void A_page_break_starts_a_new_paragraph()
    {
        var docx = WithBody($"<w:p>{Run("Side én")}<w:r><w:br w:type=\"page\"/></w:r>{Run("Side to")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Side én\n\nSide to", markdown);
    }

    // A tab at a line's start would make the line a code block.
    [Fact]
    public void A_tab_becomes_a_space()
    {
        var docx = WithBody($"<w:p><w:r><w:tab/></w:r>{Run("Navn:")}<w:r><w:tab/></w:r>{Run("Ola")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Navn: Ola", markdown);
    }

    // DocumentOutline makes sections from headings, so plain text must not start one.
    [Theory]
    [InlineData("# Ikke en overskrift", "\\# Ikke en overskrift")]
    [InlineData("> Ikke et sitat", "\\> Ikke et sitat")]
    [InlineData("   #3 på lista", "\\#3 på lista")]
    public void A_plain_paragraph_starting_with_a_block_marker_stays_text(string text, string expected)
    {
        var docx = WithParagraphs(text);

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void A_line_after_a_line_break_starting_with_a_hash_stays_text()
    {
        var docx = WithBody($"<w:p>{Run("Første linje")}<w:r><w:br/></w:r>{Run("# andre linje")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Første linje  \n\\# andre linje", markdown);
    }

    // Tracked changes are read as accepted.
    [Fact]
    public void Inserted_text_is_kept_and_deleted_and_moved_away_text_is_not()
    {
        var docx = WithBody(
            "<w:p>" +
            $"<w:ins w:id=\"1\" w:author=\"A\">{Run("ny ")}</w:ins>" +
            "<w:del w:id=\"2\" w:author=\"A\"><w:r><w:tab/><w:delText>slettet </w:delText></w:r></w:del>" +
            $"<w:moveFrom w:id=\"3\" w:author=\"A\">{Run("flyttet ")}</w:moveFrom>" +
            $"<w:moveTo w:id=\"4\" w:author=\"A\">{Run("hit")}</w:moveTo>" +
            "</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("ny hit", markdown);
    }

    [Fact]
    public void A_field_shows_its_result_not_its_code()
    {
        var docx = WithBody(
            "<w:p>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>" +
            $"<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>{Run("3")}<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("3", markdown);
    }

    [Theory]
    [InlineData("<w:vanish/>")]
    [InlineData("<w:webHidden/>")]
    public void Hidden_text_is_not_output(string hiddenProperty)
    {
        var docx = WithBody($"<w:p>{Run("Synlig")}{Run(" skjult", hiddenProperty)}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Synlig", markdown);
    }

    [Fact]
    public void Text_switched_back_to_visible_is_output()
    {
        var docx = WithBody($"<w:p>{Run("Synlig", "<w:vanish w:val=\"0\"/>")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Synlig", markdown);
    }

    // Undoc and MarkItDotNet drop these, and with them all of a form's answers.
    [Fact]
    public void Text_in_a_content_control_is_kept_with_its_checkbox()
    {
        var docx = WithBody($"<w:p><w:sdt><w:sdtPr><w:alias w:val=\"Avkrysning\"/></w:sdtPr><w:sdtContent>{Run("☒")}</w:sdtContent></w:sdt>{Run(" Ja")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("☒ Ja", markdown);
    }

    [Fact]
    public void A_content_control_showing_its_placeholder_outputs_nothing()
    {
        var docx = WithBody($"<w:p>{Run("Navn: ")}<w:sdt><w:sdtPr><w:showingPlcHdr/></w:sdtPr><w:sdtContent>{Run("Klikk her for å skrive inn tekst.")}</w:sdtContent></w:sdt></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Navn:", markdown);
    }

    [Fact]
    public void A_block_content_control_keeps_its_paragraphs()
    {
        var docx = WithBody($"<w:sdt><w:sdtContent>{Paragraph("Inni")}</w:sdtContent></w:sdt>{Paragraph("Utenfor")}");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Inni\n\nUtenfor", markdown);
    }

    // A text box is a whole paragraph nested inside a run of the outer paragraph.
    [Fact]
    public void A_text_box_is_output_once_after_its_paragraph()
    {
        var docx = WithBody(
            "<w:p>" +
            $"<w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:textbox><w:txbxContent>{Paragraph("Boks")}</w:txbxContent></v:textbox></v:shape></w:pict></w:r>" +
            $"{Run("Avsnitt")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Avsnitt\n\nBoks", markdown);
    }

    [Fact]
    public void A_non_breaking_hyphen_is_a_hyphen()
    {
        var docx = WithBody($"<w:p>{Run("e")}<w:r><w:noBreakHyphen/></w:r>{Run("post")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("e-post", markdown);
    }
}

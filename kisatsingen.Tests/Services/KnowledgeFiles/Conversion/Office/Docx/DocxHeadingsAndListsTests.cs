using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxHeadingsAndListsTests
{
    private const int BulletListId = 1;
    private const int NumberedListId = 2;

    // Bulleted and numbered lists with two levels, as Word defines them in numbering.xml.
    private const string Numbering =
        "<w:abstractNum w:abstractNumId=\"10\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"bullet\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:numFmt w:val=\"bullet\"/></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"20\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"decimal\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:numFmt w:val=\"lowerLetter\"/></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"10\"/></w:num>" +
        "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"20\"/></w:num>" +
        "<w:num w:numId=\"3\"><w:abstractNumId w:val=\"20\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride></w:num>";

    private const int ListStartingAtFiveId = 3;

    private static string HeadingStyle(string id, string name, int outlineLevel, string extraParagraphProperties = "") =>
        ParagraphStyle(id, name, $"<w:pPr>{extraParagraphProperties}<w:outlineLvl w:val=\"{outlineLevel}\"/></w:pPr>");

    private static string ListParagraph(int listId, int level, string text) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{listId}\"/></w:numPr></w:pPr>{Run(text)}</w:p>";

    private static byte[] WithLists(string body, string styles = "") => WithParts(body, styles: styles, numbering: Numbering);

    [Theory]
    [InlineData("Overskrift1", "heading 1", 0, "# Tittel")]
    [InlineData("Heading2", "heading 2", 1, "## Tittel")]
    [InlineData("Overskrift6", "heading 6", 5, "###### Tittel")]
    public void A_heading_style_in_norwegian_or_english_becomes_a_markdown_heading(string styleId, string name, int outlineLevel, string expected)
    {
        var docx = WithParts(StyledParagraph(styleId, "Tittel"), styles: HeadingStyle(styleId, name, outlineLevel));

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    // Markdown has six heading levels; a deeper Word heading is still a section.
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void A_heading_deeper_than_six_levels_becomes_level_six(int outlineLevel)
    {
        var docx = WithParts(StyledParagraph("Dyp", "Dyp"), styles: HeadingStyle("Dyp", "Dyp", outlineLevel));

        var markdown = ToMarkdown(docx);

        Assert.Equal("###### Dyp", markdown);
    }

    [Fact]
    public void A_title_style_without_an_outline_level_is_a_heading_by_its_built_in_name()
    {
        var docx = WithParts(StyledParagraph("Tittel", "Rapport"), styles: ParagraphStyle("Tittel", "Title"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("# Rapport", markdown);
    }

    [Fact]
    public void A_paragraph_s_own_outline_level_makes_it_a_heading()
    {
        var docx = WithBody($"<w:p><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr>{Run("Bakgrunn")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("### Bakgrunn", markdown);
    }

    [Fact]
    public void A_paragraph_marked_as_body_text_is_no_heading_even_in_a_heading_style()
    {
        var body = $"<w:p><w:pPr><w:pStyle w:val=\"Overskrift1\"/><w:outlineLvl w:val=\"9\"/></w:pPr>{Run("Brødtekst")}</w:p>";
        var docx = WithParts(body, styles: HeadingStyle("Overskrift1", "heading 1", 0));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Brødtekst", markdown);
    }

    [Fact]
    public void A_heading_s_line_breaks_become_spaces()
    {
        var docx = WithBody($"<w:p><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>{Run("Del én")}<w:r><w:br/></w:r>{Run("om budsjett")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("# Del én om budsjett", markdown);
    }

    [Fact]
    public void Bulleted_and_numbered_lists_nest_two_levels()
    {
        var docx = WithLists(
            ListParagraph(BulletListId, 0, "Epler") + ListParagraph(BulletListId, 1, "Røde") +
            Paragraph("Mellom") +
            ListParagraph(NumberedListId, 0, "Første") + ListParagraph(NumberedListId, 1, "Under"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("- Epler\n    - Røde\n\nMellom\n\n1. Første\n    1. Under", markdown);
    }

    // Word's "List Bullet" style puts the paragraph in a list without numPr of its own.
    [Fact]
    public void A_list_item_inherits_its_list_from_its_style()
    {
        var style = ParagraphStyle("Punktliste", "List Bullet", $"<w:pPr><w:numPr><w:numId w:val=\"{BulletListId}\"/></w:numPr></w:pPr>");
        var docx = WithLists(StyledParagraph("Punktliste", "Punkt"), style);

        var markdown = ToMarkdown(docx);

        Assert.Equal("- Punkt", markdown);
    }

    // Numbered headings ("1.1 Bakgrunn") put numPr in the heading styles.
    [Fact]
    public void A_numbered_heading_is_a_heading_without_a_list_marker()
    {
        var style = HeadingStyle("Overskrift1", "heading 1", 0, $"<w:numPr><w:numId w:val=\"{NumberedListId}\"/></w:numPr>");
        var docx = WithLists(StyledParagraph("Overskrift1", "Bakgrunn"), style);

        var markdown = ToMarkdown(docx);

        Assert.Equal("# Bakgrunn", markdown);
    }

    // The model reads the Markdown as text, so "punkt 2" needs the 2.
    [Fact]
    public void A_numbered_list_s_items_get_their_numbers()
    {
        var docx = WithLists(ListParagraph(NumberedListId, 0, "Første") + ListParagraph(NumberedListId, 0, "Andre") + ListParagraph(NumberedListId, 0, "Tredje"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("1. Første\n2. Andre\n3. Tredje", markdown);
    }

    [Fact]
    public void A_numbered_list_starts_at_its_start_value()
    {
        var docx = WithLists(ListParagraph(ListStartingAtFiveId, 0, "Fem") + ListParagraph(ListStartingAtFiveId, 0, "Seks"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("5. Fem\n6. Seks", markdown);
    }

    // Word restarts the "a." under each new item.
    [Fact]
    public void A_new_item_restarts_the_numbering_of_the_level_below_it()
    {
        var docx = WithLists(
            ListParagraph(NumberedListId, 0, "Første") + ListParagraph(NumberedListId, 1, "a") + ListParagraph(NumberedListId, 1, "b") +
            ListParagraph(NumberedListId, 0, "Andre") + ListParagraph(NumberedListId, 1, "a igjen"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("1. Første\n    1. a\n    2. b\n2. Andre\n    1. a igjen", markdown);
    }

    // As Word counts it: an indented paragraph between two items doesn't restart the list.
    [Fact]
    public void A_list_continues_its_numbering_past_a_paragraph_between_its_items()
    {
        var docx = WithLists(ListParagraph(NumberedListId, 0, "Første") + Paragraph("Fortsettelse") + ListParagraph(NumberedListId, 0, "Andre"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("1. Første\n\nFortsettelse\n\n2. Andre", markdown);
    }

    [Fact]
    public void A_paragraph_in_list_zero_is_in_no_list()
    {
        var docx = WithLists(ListParagraph(0, 0, "Ingen liste"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Ingen liste", markdown);
    }

    // Eight spaces after "- " would make a code block.
    [Fact]
    public void A_list_skipping_a_level_is_indented_one_level_deeper_than_the_item_before()
    {
        var docx = WithLists(ListParagraph(BulletListId, 0, "Topp") + ListParagraph(BulletListId, 2, "Dypt"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("- Topp\n    - Dypt", markdown);
    }

    [Fact]
    public void A_list_item_starting_with_a_hash_stays_text()
    {
        var docx = WithLists(ListParagraph(BulletListId, 0, "#1 prioritet"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("- \\#1 prioritet", markdown);
    }
}

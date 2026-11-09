using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxNotesAndLinksTests
{
    // Word writes these two notes first in every footnotes.xml: the lines drawn above the notes.
    private const string SeparatorNotes =
        "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>" +
        "<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>";

    private static string Footnote(int id, params string[] paragraphs) =>
        $"<w:footnote w:id=\"{id}\">{string.Concat(paragraphs.Select(Paragraph))}</w:footnote>";

    private static string FootnoteReference(int id) => $"<w:r><w:footnoteReference w:id=\"{id}\"/></w:r>";

    [Fact]
    public void A_footnote_becomes_a_reference_and_a_definition_after_the_body()
    {
        var body = $"<w:p>{Run("Tekst")}{FootnoteReference(1)}</w:p>{Paragraph("Mer tekst")}";
        var docx = WithParts(body, footnotes: SeparatorNotes + Footnote(1, "Kilde: SSB"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst[^1]\n\nMer tekst\n\n[^1]: Kilde: SSB", markdown);
    }

    [Fact]
    public void A_footnote_s_further_paragraphs_are_indented_under_its_definition()
    {
        var docx = WithParts($"<w:p>{Run("Tekst")}{FootnoteReference(1)}</w:p>", footnotes: Footnote(1, "Første", "Andre"));

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst[^1]\n\n[^1]: Første\n\n    Andre", markdown);
    }

    [Fact]
    public void Separator_notes_are_not_output()
    {
        var docx = WithParts(Paragraph("Tekst"), footnotes: SeparatorNotes);

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst", markdown);
    }

    [Fact]
    public void A_reference_to_a_missing_note_is_left_out()
    {
        var docx = WithParts($"<w:p>{Run("Tekst")}{FootnoteReference(7)}</w:p>", footnotes: SeparatorNotes);

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst", markdown);
    }

    [Fact]
    public void An_endnote_gets_a_label_apart_from_the_footnotes()
    {
        var body = $"<w:p>{Run("Tekst")}{FootnoteReference(1)}<w:r><w:endnoteReference w:id=\"1\"/></w:r></w:p>";
        var docx = WithParts(body, footnotes: Footnote(1, "Fotnote"), endnotes: $"<w:endnote w:id=\"1\">{Paragraph("Sluttnote")}</w:endnote>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Tekst[^1][^e1]\n\n[^1]: Fotnote\n\n[^e1]: Sluttnote", markdown);
    }

    [Fact]
    public void A_hyperlink_becomes_a_markdown_link()
    {
        var body = $"<w:p>{Run("Les ")}<w:hyperlink r:id=\"rIdLink\">{Run("søknaden", "<w:b/>")}</w:hyperlink></w:p>";
        var docx = WithParts(body, hyperlinks: new Dictionary<string, string> { ["rIdLink"] = "https://www.vestfoldfylke.no/soknad (ny)" });

        var markdown = ToMarkdown(docx);

        Assert.Equal("Les [**søknaden**](https://www.vestfoldfylke.no/soknad%20%28ny%29)", markdown);
    }

    [Fact]
    public void A_link_to_a_bookmark_stays_plain_text()
    {
        var docx = WithBody($"<w:p><w:hyperlink w:anchor=\"_Toc1\">{Run("Innledning")}</w:hyperlink></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Innledning", markdown);
    }

    [Fact]
    public void Brackets_in_a_link_s_text_are_escaped()
    {
        var body = $"<w:p><w:hyperlink r:id=\"rIdLink\">{Run("[Vedlegg]")}</w:hyperlink></w:p>";
        var docx = WithParts(body, hyperlinks: new Dictionary<string, string> { ["rIdLink"] = "https://example.com/" });

        var markdown = ToMarkdown(docx);

        Assert.Equal("[\\[Vedlegg\\]](https://example.com/)", markdown);
    }
}

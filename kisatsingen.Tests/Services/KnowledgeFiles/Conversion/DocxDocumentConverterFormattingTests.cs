using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

// How bold, italic and strikethrough in Word come out as Markdown.
public sealed class DocxDocumentConverterFormattingTests
{
    // As Norwegian Word names them: Sterk is "Strong", Utheving is "Emphasis".
    private static readonly string Sterk = CharacterStyle("Sterk", "Strong", "<w:b/>");
    private static readonly string Utheving = CharacterStyle("Utheving", "Emphasis", "<w:i/>");
    private static readonly string SterkUtheving = CharacterStyle("SterkUtheving", "Strong Emphasis", "<w:b/><w:i/>");
    private static readonly string Utgatt = ParagraphStyle("Utgatt", "Utgått", "<w:rPr><w:strike/></w:rPr>");
    private static readonly string FetAvsnitt = ParagraphStyle("FetAvsnitt", "Fet avsnitt", "<w:rPr><w:b/></w:rPr>");
    private static readonly string Overskrift1 = ParagraphStyle("Overskrift1", "heading 1", "<w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/></w:rPr>");

    private static async Task<string> ConvertToMarkdownAsync(byte[] docx)
    {
        var converter = new DocxDocumentConverter(NullLogger<DocxDocumentConverter>.Instance, new AttachmentOptions());
        var request = new ConversionRequest("dokument.docx", AttachmentContentTypes.Docx.ContentType, new MemoryStream(docx), int.MaxValue);

        return Assert.IsType<ConversionResult.Converted>(await converter.ConvertAsync(request, CancellationToken.None)).Markdown;
    }

    private static byte[] WithStyles(string body) => WithParts(body, styles: string.Concat(Sterk, Utheving, SterkUtheving, Utgatt, FetAvsnitt, Overskrift1));

    private static string StyledRun(string styleId, string text) => Run(text, $"<w:rStyle w:val=\"{styleId}\"/>");

    [Theory]
    [InlineData("<w:b/>", "**tekst**")]
    [InlineData("<w:i/>", "*tekst*")]
    [InlineData("<w:strike/>", "~~tekst~~")]
    [InlineData("<w:dstrike/>", "~~tekst~~")]
    [InlineData("<w:b/><w:i/>", "***tekst***")]
    [InlineData("<w:i/><w:strike/>", "*~~tekst~~*")]
    public async Task A_run_s_own_formatting_becomes_markdown_markers(string runProperties, string expected)
    {
        var docx = WithBody($"<w:p>{Run("tekst", runProperties)}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    [Theory]
    [InlineData("Sterk", "**tekst**")]
    [InlineData("Utheving", "*tekst*")]
    [InlineData("SterkUtheving", "***tekst***")]
    public async Task A_character_style_makes_its_text_bold_or_italic(string styleId, string expected)
    {
        var docx = WithStyles($"<w:p>{StyledRun(styleId, "tekst")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public async Task A_paragraph_style_formats_the_text_of_its_runs()
    {
        var docx = WithStyles(StyledParagraph("Utgatt", "Utgått tekst"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~Utgått tekst~~", markdown);
    }

    // Word writes this when bold from a style is switched off for some text.
    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public async Task A_run_switching_off_bold_is_not_bold_even_when_its_style_is(string value)
    {
        var docx = WithStyles($"<w:p><w:pPr><w:pStyle w:val=\"FetAvsnitt\"/></w:pPr>{Run("Vanlig", $"<w:b w:val=\"{value}\"/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Vanlig", markdown);
    }

    // The prefix is the writing tool's choice; only the namespace says what an element is.
    [Fact]
    public async Task Formatting_is_recognised_whatever_namespace_prefix_the_document_uses()
    {
        var docx = WithBody($"<w:p><x:r xmlns:x=\"{WordNamespace}\"><x:rPr><x:b/></x:rPr><x:t>tekst</x:t></x:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("**tekst**", markdown);
    }

    // MarkItDotNet wrote "**H****envisning**".
    [Fact]
    public async Task Neighbouring_runs_with_the_same_formatting_become_one_span()
    {
        var docx = WithBody($"<w:p>{Run("H", "<w:b/>")}{Run("envisning", "<w:b/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("**Henvisning**", markdown);
    }

    [Fact]
    public async Task A_struck_paragraph_split_into_several_runs_is_struck_through_as_a_whole()
    {
        var docx = WithBody($"<w:p>{Run("Utgått ", "<w:strike/>")}{Run("tekst", "<w:strike/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~Utgått tekst~~", markdown);
    }

    // Markdown ignores "~~utgått ~~", so the markers go inside the whitespace.
    [Fact]
    public async Task Markers_sit_outside_the_spaces_around_the_formatted_text()
    {
        var docx = WithBody($"<w:p>{Run("Dette er")}{Run(" utgått ", "<w:strike/>")}{Run("tekst")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Dette er ~~utgått~~ tekst", markdown);
    }

    [Fact]
    public async Task Separate_struck_words_are_struck_through_separately()
    {
        var docx = WithBody($"<w:p>{Run("gammel", "<w:strike/>")}{Run(" og ")}{Run("utgått", "<w:strike/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~gammel~~ og ~~utgått~~", markdown);
    }

    // CommonMark leaves "**org.nr.**929" as literal asterisks.
    [Fact]
    public async Task Bold_ending_in_punctuation_before_a_letter_still_renders_as_bold()
    {
        var docx = WithBody($"<w:p>{Run("org.nr.", "<w:b/>")}{Run("929")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("**org.nr.** 929", markdown);
    }

    [Fact]
    public async Task Bold_starting_with_punctuation_after_a_letter_still_renders_as_bold()
    {
        var docx = WithBody($"<w:p>{Run("Se")}{Run("(viktig)", "<w:b/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Se **(viktig)**", markdown);
    }

    [Fact]
    public async Task Italic_inside_bold_is_nested_rather_than_closed_and_reopened()
    {
        var docx = WithBody($"<w:p>{Run("Fet ", "<w:b/>")}{Run("og kursiv", "<w:b/><w:i/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("**Fet *og kursiv***", markdown);
    }

    // Selecting a word to the end of the line in Word also strikes the paragraph mark.
    [Fact]
    public async Task Only_the_struck_words_are_struck_through_when_the_paragraph_mark_is_struck()
    {
        var docx = WithBody($"<w:p><w:pPr><w:rPr><w:strike/></w:rPr></w:pPr>{Run("Dette er ")}{Run("utgått", "<w:strike/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Dette er ~~utgått~~", markdown);
    }

    // The # already says the text is emphasized.
    [Fact]
    public async Task A_heading_s_text_has_no_bold_or_italic_markers()
    {
        var docx = WithStyles($"<w:p><w:pPr><w:pStyle w:val=\"Overskrift1\"/></w:pPr>{Run("Innledning", "<w:i/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("# Innledning", markdown);
    }

    [Fact]
    public async Task A_struck_heading_stays_struck()
    {
        var docx = WithStyles($"<w:p><w:pPr><w:pStyle w:val=\"Overskrift1\"/></w:pPr>{Run("Utgått", "<w:strike/>")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("# ~~Utgått~~", markdown);
    }

    [Fact]
    public async Task A_struck_text_box_does_not_strike_through_the_paragraph_around_it()
    {
        var textBox = $"<w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:textbox><w:txbxContent><w:p>{Run("Boks", "<w:strike/>")}</w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r>";
        var docx = WithBody($"<w:p>{textBox}{Run("Avsnitt")}</w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Avsnitt\n\n~~Boks~~", markdown);
    }
}

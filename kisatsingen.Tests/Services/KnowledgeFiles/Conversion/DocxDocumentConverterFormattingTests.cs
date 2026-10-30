using ElBruno.MarkItDotNet.Converters;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ConverterRegistry = ElBruno.MarkItDotNet.ConverterRegistry;
using MarkdownService = ElBruno.MarkItDotNet.MarkdownService;
using MarkItDotNetOptions = ElBruno.MarkItDotNet.MarkItDotNetOptions;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

// How Word styles and formatting come out as Markdown
public sealed class DocxDocumentConverterFormattingTests
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly MarkdownService MarkdownService = CreateMarkdownService();

    // Registered the same way as in Program.cs.
    private static MarkdownService CreateMarkdownService()
    {
        var registry = new ConverterRegistry();
        registry.Register(new DocxConverter());
        return new MarkdownService(registry, [], new MarkItDotNetOptions());
    }

    private static async Task<string> ConvertToMarkdownAsync(byte[] docx)
    {
        var converter = new DocxDocumentConverter(NullLogger<DocxDocumentConverter>.Instance, MarkdownService, new AttachmentOptions());
        var request = new ConversionRequest("dokument.docx", AttachmentContentTypes.Docx.ContentType, new MemoryStream(docx), int.MaxValue);

        return Assert.IsType<ConversionResult.Converted>(await converter.ConvertAsync(request, CancellationToken.None)).Markdown;
    }

    [Theory]
    [InlineData("Heading1", "# Tittel")]
    [InlineData("Heading3", "### Tittel")]
    [InlineData("Overskrift1", "# Tittel")]
    [InlineData("Overskrift2", "## Tittel")]
    [InlineData("Overskrift3", "### Tittel")]
    [InlineData("Overskrift4", "#### Tittel")]
    [InlineData("Overskrift5", "##### Tittel")]
    [InlineData("Overskrift6", "###### Tittel")]
    [InlineData("Tittel", "# Tittel")]
    [InlineData("Undertittel", "## Tittel")]
    public async Task Norwegian_and_english_heading_styles_become_markdown_headings(string style, string expected)
    {
        var docx = WithBody(StyledParagraph(style, "Tittel"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    // Markdown has six heading levels, so the deeper Norwegian ones share the last.
    [Theory]
    [InlineData("Overskrift7")]
    [InlineData("Overskrift8")]
    [InlineData("Overskrift9")]
    public async Task Headings_deeper_than_six_levels_become_level_six(string style)
    {
        var docx = WithBody(StyledParagraph(style, "Dyp"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("###### Dyp", markdown);
    }

    [Theory]
    [InlineData("Sterk", "**tekst**")]
    [InlineData("Sterkreferanse", "**tekst**")]
    [InlineData("Utheving", "*tekst*")]
    [InlineData("Svakutheving", "*tekst*")]
    [InlineData("Boktittel", "*tekst*")]
    [InlineData("Svakreferanse", "*tekst*")]
    [InlineData("Sterkutheving", "***tekst***")]
    public async Task Norwegian_character_styles_become_bold_or_italic(string runStyle, string expected)
    {
        var docx = WithBody(StyledRun(runStyle, "tekst"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    // Word writes "<w:rStyle .../>" but other tools add a space before "/>".
    [Fact]
    public async Task A_character_style_is_recognised_with_a_space_before_the_self_closing_slash()
    {
        var docx = WithBody("<w:p><w:r><w:rPr><w:rStyle w:val=\"Sterk\" /></w:rPr><w:t>tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("**tekst**", markdown);
    }

    [Theory]
    [InlineData("Sitat", "> *Sitert tekst*")]
    [InlineData("Sterktsitat", "> **Sitert tekst**")]
    public async Task Quote_styles_become_markdown_quotes(string style, string expected)
    {
        var docx = WithBody(StyledParagraph(style, "Sitert tekst"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    // A quote must not depend on the document also having a Norwegian heading
    // or character style somewhere in the same part.
    [Fact]
    public async Task A_quote_in_a_document_with_a_norwegian_heading_becomes_a_markdown_quote()
    {
        var docx = WithBody(StyledParagraph("Overskrift1", "Innledning") + StyledParagraph("Sitat", "Sitert tekst"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("# Innledning\n\n> *Sitert tekst*", markdown);
    }

    // Word nearly always writes run properties, such as the language, on both
    // the paragraph mark and the run. They say nothing about strikethrough.
    [Theory]
    [InlineData("<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr><w:r><w:rPr><w:lang w:val=\"nb-NO\"/></w:rPr><w:t>Sitert tekst</w:t></w:r></w:p>")]
    [InlineData("<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/><w:rPr><w:lang w:val=\"nb-NO\"/></w:rPr></w:pPr><w:r><w:t>Sitert tekst</w:t></w:r></w:p>")]
    public async Task A_quote_with_run_properties_other_than_strikethrough_is_not_struck_through(string body)
    {
        var docx = WithBody(body);

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("> *Sitert tekst*", markdown);
    }

    // Word splits a paragraph into several runs at every edit or spell check.
    [Fact]
    public async Task A_quote_split_into_several_runs_is_quoted_as_a_whole()
    {
        var docx = WithBody("<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Sitert </w:t></w:r><w:r><w:t>tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("> *Sitert tekst*", markdown);
    }

    [Fact]
    public async Task A_struck_paragraph_split_into_several_runs_is_struck_through_as_a_whole()
    {
        var docx = WithBody("<w:p><w:pPr><w:rPr><w:strike/></w:rPr></w:pPr><w:r><w:rPr><w:strike/></w:rPr><w:t xml:space=\"preserve\">Utgått </w:t></w:r><w:r><w:rPr><w:strike/></w:rPr><w:t>tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~Utgått tekst~~", markdown);
    }

    // Markdown only reads "> " at the start of the line, so the strikethrough goes inside the quote.
    [Fact]
    public async Task A_struck_quote_is_struck_through_inside_the_quote()
    {
        var docx = WithBody("<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/><w:rPr><w:strike/></w:rPr></w:pPr><w:r><w:rPr><w:strike/></w:rPr><w:t>Sitert tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("> *~~Sitert tekst~~*", markdown);
    }

    // Word writes this when a style that strikes through is switched off for one paragraph.
    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public async Task A_paragraph_with_strikethrough_switched_off_is_not_struck_through(string value)
    {
        var docx = WithBody($"<w:p><w:pPr><w:rPr><w:strike w:val=\"{value}\"/></w:rPr></w:pPr><w:r><w:rPr><w:strike w:val=\"{value}\"/></w:rPr><w:t>Vanlig tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Vanlig tekst", markdown);
    }

    [Fact]
    public async Task A_struck_paragraph_becomes_markdown_strikethrough()
    {
        var docx = WithBody(StruckParagraph("Utgått"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~Utgått~~", markdown);
    }

    // An empty quote paragraph is common: pressing Enter after a quote keeps the style.
    [Fact]
    public async Task An_empty_quote_paragraph_does_not_make_the_document_unreadable()
    {
        var docx = WithBody(StyledParagraph("Overskrift1", "Innledning") + "<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("# Innledning", markdown);
    }

    // The prefix is the writing tool's choice; only the namespace says what an element is.
    [Theory]
    [InlineData($"<w:p><w:pPr><x:pStyle xmlns:x=\"{WordNamespace}\" x:val=\"Overskrift1\"/></w:pPr><w:r><w:t>tekst</w:t></w:r></w:p>", "# tekst")]
    [InlineData($"<w:p><w:r><w:rPr><x:rStyle xmlns:x=\"{WordNamespace}\" x:val=\"Sterk\"/></w:rPr><w:t>tekst</w:t></w:r></w:p>", "**tekst**")]
    public async Task Styles_are_recognised_whatever_namespace_prefix_the_document_uses(string body, string expected)
    {
        var docx = WithBody(body);

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal(expected, markdown);
    }

    // Selecting a word to the end of the line in Word also strikes the paragraph mark.
    [Fact]
    public async Task Only_the_struck_words_are_struck_through_when_the_paragraph_mark_is_struck()
    {
        var docx = WithBody("<w:p><w:pPr><w:rPr><w:strike/></w:rPr></w:pPr><w:r><w:t xml:space=\"preserve\">Dette er </w:t></w:r><w:r><w:rPr><w:strike/></w:rPr><w:t>utgått</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Dette er ~~utgått~~", markdown);
    }

    [Fact]
    public async Task A_paragraph_whose_text_is_struck_is_struck_through_even_when_the_paragraph_mark_is_not()
    {
        var docx = WithBody("<w:p><w:r><w:rPr><w:strike/></w:rPr><w:t>Utgått tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~Utgått tekst~~", markdown);
    }

    // Markdown ignores "~~utgått ~~", so the markers go inside the whitespace.
    [Fact]
    public async Task A_struck_word_with_a_trailing_space_is_struck_through_without_the_space()
    {
        var docx = WithBody("<w:p><w:r><w:t xml:space=\"preserve\">Dette er </w:t></w:r><w:r><w:rPr><w:strike/></w:rPr><w:t xml:space=\"preserve\">utgått </w:t></w:r><w:r><w:t>tekst</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Dette er ~~utgått~~ tekst", markdown);
    }

    [Fact]
    public async Task Separate_struck_words_are_struck_through_separately()
    {
        var docx = WithBody("<w:p><w:r><w:rPr><w:strike/></w:rPr><w:t>gammel</w:t></w:r><w:r><w:t xml:space=\"preserve\"> og </w:t></w:r><w:r><w:rPr><w:strike/></w:rPr><w:t>utgått</w:t></w:r></w:p>");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("~~gammel~~ og ~~utgått~~", markdown);
    }

    // A text box is a whole paragraph nested inside a run of the outer paragraph.
    [Fact]
    public async Task A_quote_anchoring_a_text_box_is_quoted_around_its_own_text()
    {
        var docx = WithBody(QuoteWithTextBox("<w:r><w:t>Boks</w:t></w:r>"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Contains("> *Sitert tekst*", markdown);
    }

    [Fact]
    public async Task A_struck_text_box_does_not_strike_through_the_paragraph_around_it()
    {
        var docx = WithBody(QuoteWithTextBox("<w:r><w:rPr><w:strike/></w:rPr><w:t>Boks</w:t></w:r>"));

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Contains("> *Sitert tekst*", markdown);
    }

    private static string QuoteWithTextBox(string textBoxRuns) =>
        "<w:p><w:pPr><w:pStyle w:val=\"Sitat\"/></w:pPr>" +
        $"<w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:textbox><w:txbxContent><w:p>{textBoxRuns}</w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r>" +
        "<w:r><w:t>Sitert tekst</w:t></w:r></w:p>";
}

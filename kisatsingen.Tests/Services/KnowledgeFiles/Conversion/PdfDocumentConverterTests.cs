using System.Text;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class PdfDocumentConverterTests
{
    private const int GenerousTokenCap = 1_000_000;

    // Nine characters, estimated at three tokens (three characters per token).
    private const string ThreeTokenText = "abcdefghi";
    private const int ThreeTokens = 3;

    private static Task<ConversionResult> ConvertAsync(byte[] pdf, int maxEstimatedTokens = GenerousTokenCap, CancellationToken ct = default) =>
        new PdfDocumentConverter(NullLogger<PdfDocumentConverter>.Instance).ConvertAsync(
            new ConversionRequest("dokument.pdf", AttachmentContentTypes.Pdf.ContentType, new MemoryStream(pdf), maxEstimatedTokens),
            ct);

    [Fact]
    public async Task A_pdf_whose_pages_have_no_content_is_refused_as_empty()
    {
        var pdf = TestPdf.Pages(new BlankPage(), new BlankPage());

        var result = await ConvertAsync(pdf);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.EmptyPdf), result);
    }

    // A scan is pictures of text, with no text layer for PdfPig to read.
    [Fact]
    public async Task A_scanned_pdf_with_only_images_is_refused_as_empty()
    {
        var pdf = TestPdf.Pages(new ScannedPage(), new ScannedPage(), new ScannedPage());

        var result = await ConvertAsync(pdf);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.EmptyPdf), result);
    }

    [Fact]
    public async Task A_pdf_with_text_becomes_markdown_from_a_pdf()
    {
        var pdf = TestPdf.Pages(new TextPage("Budsjett for 2027"));

        var result = await ConvertAsync(pdf);

        var converted = Assert.IsType<ConversionResult.Converted>(result);
        Assert.Equal(("Budsjett for 2027", ContentOrigin.Pdf), (converted.Markdown, converted.Origin));
    }

    [Fact]
    public async Task Norwegian_letters_come_through_unchanged()
    {
        var pdf = TestPdf.Pages(new TextPage("Blåbærsyltetøy på Ås, ÆØÅ"));

        var result = await ConvertAsync(pdf);

        Assert.Equal("Blåbærsyltetøy på Ås, ÆØÅ", Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task The_page_count_is_the_number_of_pages_in_the_pdf(int pageCount)
    {
        var pdf = TestPdf.Pages([.. Enumerable.Range(1, pageCount).Select(page => new TextPage($"Side {page}"))]);

        var result = await ConvertAsync(pdf);

        Assert.Equal(pageCount, Assert.IsType<ConversionResult.Converted>(result).PageCount);
    }

    // The count is of the document, so a reference to "side 3" still matches
    // the original when pages before it had no text.
    [Fact]
    public async Task The_page_count_includes_blank_and_scanned_pages()
    {
        var pdf = TestPdf.Pages(new TextPage("Innledning"), new BlankPage(), new ScannedPage(), new TextPage("Avslutning"));

        var result = await ConvertAsync(pdf);

        Assert.Equal(4, Assert.IsType<ConversionResult.Converted>(result).PageCount);
    }

    [Fact]
    public async Task Pages_with_text_are_separated_by_a_horizontal_rule()
    {
        var pdf = TestPdf.Pages(new TextPage("Side en"), new TextPage("Side to"));

        var result = await ConvertAsync(pdf);

        Assert.Equal(
            $"Side en{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}Side to",
            Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    // Only the pages with text are kept, so a blank or scanned page leaves no
    // empty section behind.
    [Fact]
    public async Task Pages_without_text_add_no_separator_of_their_own()
    {
        var pdf = TestPdf.Pages(new TextPage("Side en"), new BlankPage(), new ScannedPage(), new TextPage("Side fire"));

        var result = await ConvertAsync(pdf);

        Assert.Equal(
            $"Side en{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}Side fire",
            Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    // A scanned cover page in front of the text is common.
    [Fact]
    public async Task A_pdf_starting_with_a_page_without_text_starts_with_the_first_text()
    {
        var pdf = TestPdf.Pages(new ScannedPage(), new TextPage("Innhold"));

        var result = await ConvertAsync(pdf);

        Assert.Equal("Innhold", Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    [Fact]
    public async Task A_pdf_with_more_text_than_the_token_cap_is_refused_as_too_many_tokens()
    {
        var pdf = TestPdf.Pages(new TextPage(ThreeTokenText));

        var result = await ConvertAsync(pdf, maxEstimatedTokens: ThreeTokens - 1);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.TooManyTokens(ThreeTokens - 1)), result);
    }

    [Fact]
    public async Task A_pdf_with_exactly_as_much_text_as_the_token_cap_is_converted()
    {
        var pdf = TestPdf.Pages(new TextPage(ThreeTokenText));

        var result = await ConvertAsync(pdf, maxEstimatedTokens: ThreeTokens);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    // The pipeline checks the finished Markdown against the same cap, so the
    // converter must not refuse a file the pipeline would accept. Eleven
    // characters are three tokens; one more character, like a line break left
    // at the end while converting, would make four.
    [Fact]
    public async Task A_pdf_the_pipeline_would_accept_is_not_refused_for_its_size()
    {
        const string elevenCharacters = "abcdefghijk";
        var pdf = TestPdf.Pages(new TextPage(elevenCharacters));

        var result = await ConvertAsync(pdf, maxEstimatedTokens: ThreeTokens);

        Assert.Equal(elevenCharacters, Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    // Each page alone is under the cap; only the document as a whole is over.
    [Fact]
    public async Task The_token_cap_applies_to_the_text_of_all_pages_together()
    {
        var pdf = TestPdf.Pages(new TextPage(ThreeTokenText), new TextPage(ThreeTokenText));

        var result = await ConvertAsync(pdf, maxEstimatedTokens: ThreeTokens * 2 - 1);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.TooManyTokens(ThreeTokens * 2 - 1)), result);
    }

    [Fact]
    public async Task Pages_without_text_do_not_count_towards_the_token_cap()
    {
        var pdf = TestPdf.Pages(new ScannedPage(), new TextPage(ThreeTokenText), new BlankPage());

        var result = await ConvertAsync(pdf, maxEstimatedTokens: ThreeTokens);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    // Passes the upload's signature check, but is not a PDF past the header.
    [Fact]
    public async Task A_file_that_only_starts_like_a_pdf_is_refused_as_unreadable()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nDette er ikke en PDF.");

        var result = await ConvertAsync(pdf);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ErrorParsingPdf), result);
    }

    [Fact]
    public async Task A_password_protected_pdf_is_refused_as_unreadable()
    {
        var pdf = TestPdf.PasswordProtected();

        var result = await ConvertAsync(pdf);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ErrorParsingPdf), result);
    }

    // The pipeline needs the cancellation itself, not a rejection, to know the
    // user stopped rather than the file being bad.
    [Fact]
    public async Task A_cancelled_conversion_throws_rather_than_rejecting_the_file()
    {
        var pdf = TestPdf.Pages(new TextPage("Innhold"));

        var conversion = ConvertAsync(pdf, ct: new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversion);
    }
}

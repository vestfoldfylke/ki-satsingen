using System.Text;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class TextDocumentConverterTests
{
    private const int GenerousTokenCap = 1_000_000;

    private static Task<ConversionResult> ConvertAsync(string text, string contentType, int maxEstimatedTokens = GenerousTokenCap) =>
        new TextDocumentConverter().ConvertAsync(
            new ConversionRequest("fil", contentType, new MemoryStream(Encoding.UTF8.GetBytes(text)), maxEstimatedTokens),
            CancellationToken.None);

    [Fact]
    public async Task A_markdown_file_is_used_as_it_is_as_a_text_file()
    {
        var result = await ConvertAsync("# Tittel\n\nTekst", AttachmentContentTypes.Markdown.ContentType);

        Assert.Equal(new ConversionResult.Converted("# Tittel\n\nTekst", ContentOrigin.TextFile, PageCount: null), result);
    }

    [Fact]
    public async Task A_plain_text_file_is_used_as_it_is_as_a_text_file()
    {
        var result = await ConvertAsync("Bare tekst", AttachmentContentTypes.PlainText.ContentType);

        Assert.Equal(new ConversionResult.Converted("Bare tekst", ContentOrigin.TextFile, PageCount: null), result);
    }

    // Refused on its byte count alone, before it is read or decoded.
    [Fact]
    public async Task A_file_too_large_for_the_token_cap_however_it_is_decoded_is_refused_before_reading()
    {
        var result = await ConvertAsync(new string('a', 120), AttachmentContentTypes.PlainText.ContentType, maxEstimatedTokens: 5);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.TooManyTokens(5)), result);
    }
}

using System.IO.Compression;
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

public sealed class DocxDocumentConverterTests
{
    private const int GenerousTokenCap = 1_000_000;

    // Nine characters, estimated at three tokens (three characters per token).
    private const string ThreeTokenText = "abcdefghi";
    private const int ThreeTokens = 3;

    // A small upload limit keeps the unpacked limit, four times it, easy to reason about.
    private const uint OneMegabyte = 1024 * 1024;

    private static readonly MarkdownService MarkdownService = CreateMarkdownService();

    // Registered the same way as in Program.cs.
    private static MarkdownService CreateMarkdownService()
    {
        var registry = new ConverterRegistry();
        registry.Register(new DocxConverter());
        return new MarkdownService(registry, [], new MarkItDotNetOptions());
    }

    private static DocxDocumentConverter CreateConverter(long maxFileBytes = OneMegabyte) =>
        new(NullLogger<DocxDocumentConverter>.Instance, MarkdownService, new AttachmentOptions { MaxFileBytes = maxFileBytes });

    private static Task<ConversionResult> ConvertAsync(byte[] docx, int maxEstimatedTokens = GenerousTokenCap, long maxFileBytes = OneMegabyte) =>
        CreateConverter(maxFileBytes).ConvertAsync(
            new ConversionRequest("dokument.docx", AttachmentContentTypes.Docx.ContentType, new MemoryStream(docx), maxEstimatedTokens),
            CancellationToken.None);

    private static async Task<string> ConvertToMarkdownAsync(byte[] docx) =>
        Assert.IsType<ConversionResult.Converted>(await ConvertAsync(docx)).Markdown;

    [Fact]
    public void The_converter_handles_the_docx_content_type()
    {
        var converter = CreateConverter();

        Assert.Equal([AttachmentContentTypes.Docx.ContentType], converter.ContentTypes);
    }

    [Fact]
    public async Task A_docx_with_text_becomes_markdown_from_a_docx()
    {
        var docx = WithParagraphs("Budsjett for 2027");

        var result = await ConvertAsync(docx);

        Assert.Equal(new ConversionResult.Converted("Budsjett for 2027", ContentOrigin.Docx, null), result);
    }

    [Fact]
    public async Task Norwegian_letters_come_through_unchanged()
    {
        var docx = WithParagraphs("Blåbærsyltetøy på Ås, ÆØÅ");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Blåbærsyltetøy på Ås, ÆØÅ", markdown);
    }

    [Fact]
    public async Task Paragraphs_are_separated_by_a_blank_line()
    {
        var docx = WithParagraphs("Første avsnitt", "Andre avsnitt");

        var markdown = await ConvertToMarkdownAsync(docx);

        Assert.Equal("Første avsnitt\n\nAndre avsnitt", markdown);
    }

    [Theory]
    [InlineData("<w:p/>")]
    [InlineData("")]
    public async Task A_docx_without_text_is_refused_as_empty(string body)
    {
        var docx = WithBody(body);

        var result = await ConvertAsync(docx);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.EmptyDocx), result);
    }

    [Fact]
    public async Task A_file_that_is_not_a_zip_archive_is_refused_as_unreadable()
    {
        var docx = "Dette er ikke et Word-dokument."u8.ToArray();

        var result = await ConvertAsync(docx);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ErrorParsingDocx), result);
    }

    // Passes the upload's signature check, since every zip starts with "PK".
    [Fact]
    public async Task A_zip_archive_that_is_not_a_word_document_is_refused()
    {
        var docx = ZipWith("notater.txt", "Ikke et Word-dokument");

        var result = await ConvertAsync(docx);

        Assert.IsType<ConversionResult.Rejected>(result);
    }

    [Fact]
    public async Task A_docx_that_unpacks_to_gigabytes_is_refused_as_too_large()
    {
        const uint threeGigabytes = 3u * 1024 * OneMegabyte;
        var docx = WithDeclaredUncompressedSizes(WithParagraphs("Kort tekst"), ("word/document.xml", threeGigabytes));

        var result = await ConvertAsync(docx);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.DocxTooLargeUncompressed), result);
    }

    // Each part alone is under the limit; only the archive as a whole is over.
    [Fact]
    public async Task The_unpacked_size_limit_applies_to_all_parts_together()
    {
        var docx = WithDeclaredUncompressedSizes(
            WithParagraphs("Kort tekst"),
            ("word/document.xml", 3 * OneMegabyte),
            ("[Content_Types].xml", 3 * OneMegabyte));

        var result = await ConvertAsync(docx, maxFileBytes: OneMegabyte);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.DocxTooLargeUncompressed), result);
    }

    // Images barely compress, so a docx near the upload limit unpacks to more
    // than the upload limit and must still be converted.
    [Fact]
    public async Task A_docx_that_unpacks_to_more_than_the_upload_limit_but_within_four_times_it_is_converted()
    {
        var docx = WithDeclaredUncompressedSizes(WithParagraphs("Kort tekst"), ("word/document.xml", 3 * OneMegabyte));

        var result = await ConvertAsync(docx, maxFileBytes: OneMegabyte);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    [Fact]
    public async Task The_unpacked_size_limit_follows_the_upload_limit()
    {
        var docx = WithDeclaredUncompressedSizes(WithParagraphs("Kort tekst"), ("word/document.xml", 5 * OneMegabyte));

        var result = await ConvertAsync(docx, maxFileBytes: 2 * OneMegabyte);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    [Fact]
    public async Task A_docx_with_more_text_than_the_token_cap_is_refused_as_too_many_tokens()
    {
        var docx = WithParagraphs(new string('a', 10_000));

        var result = await ConvertAsync(docx, maxEstimatedTokens: 100);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.TooManyTokens(100)), result);
    }

    // A docx is a zip with XML markup around the text, so the file is far larger
    // than its text. The cap is on the text, the same as the pipeline checks.
    [Fact]
    public async Task A_docx_with_exactly_as_much_text_as_the_token_cap_is_converted_whatever_the_file_size()
    {
        var docx = WithParagraphs(ThreeTokenText);

        var result = await ConvertAsync(docx, maxEstimatedTokens: ThreeTokens);

        Assert.Equal(ThreeTokenText, Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    private static byte[] ZipWith(string entryName, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
            writer.Write(content);
        }

        return buffer.ToArray();
    }
}

using System.IO.Compression;
using System.Xml.Linq;
using ElBruno.MarkItDotNet;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// NOTE: This docx document converter is not perfect. It most likely will get some syntax wrong,
// but the overall gist is preserved and understandable for further processing.
public class DocxDocumentConverter : IDocumentConverter
{
    // Images are already compressed and unpack to about their own size. XML packs
    // 10-20x, but four uploads' worth of it holds far more text than the token cap
    // allows, so only zip bombs and documents that would be refused anyway are
    // stopped. Tied to the upload limit so the two cannot drift apart.
    private const int MaxUncompressedToUploadRatio = 4;

    private readonly ILogger<DocxDocumentConverter> _logger;
    private readonly MarkdownService _markdownService;
    private readonly long _maxUncompressedBytes;

    public IReadOnlyList<string> ContentTypes { get; } = [AttachmentContentTypes.Docx.ContentType];

    public DocxDocumentConverter(ILogger<DocxDocumentConverter> logger, MarkdownService markdownService, AttachmentOptions attachmentOptions)
    {
        _logger = logger;
        _markdownService = markdownService;
        _maxUncompressedBytes = attachmentOptions.MaxFileBytes * MaxUncompressedToUploadRatio;
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        await using var docx = new MemoryStream();
        try
        {
            await request.Content.CopyToAsync(docx, ct);

            if (ExceedsMaxUncompressedBytes(docx, _maxUncompressedBytes))
            {
                _logger.LogWarning(
                    "Docx document '{Filename}' with ContentType '{ContentType}' unpacks to more than {MaxUncompressedBytes} bytes.",
                    request.FileName,
                    request.ContentType,
                    _maxUncompressedBytes
                );
                return new ConversionResult.Rejected(ConversionRejections.DocxTooLargeUncompressed);
            }

            await NormalizeDocx(docx, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to normalize docx document '{Filename}' with ContentType '{ContentType}'",
                request.FileName,
                request.ContentType
            );
            return new ConversionResult.Rejected(ConversionRejections.ErrorParsingDocx);
        }

        var result = await _markdownService.ConvertAsync(docx, ".docx", ct);

        if (!result.Success)
        {
            _logger.LogError(
                "Failed to convert docx document '{Filename}' with ContentType '{ContentType}' to Markdown. ErrorMessage: {ErrorMessage}",
                request.FileName,
                request.ContentType,
                result.ErrorMessage
            );
            return new ConversionResult.Rejected(ConversionRejections.ErrorParsingDocx);
        }

        if (result.Markdown.Length == 0)
        {
            return new ConversionResult.Rejected(ConversionRejections.EmptyDocx);
        }

        if (TokenEstimate.FromCharacters(result.Markdown.Length) <= request.MaxEstimatedTokens)
        {
            return new ConversionResult.Converted(result.Markdown, ContentOrigin.Docx, null);
        }

        _logger.LogWarning(
            "Converting docx document '{Filename}' with ContentType '{ContentType}' to Markdown would exceeded the limit for MaxEstimatedTokens ({MaxEstimatedTokens}).",
            request.FileName,
            request.ContentType,
            request.MaxEstimatedTokens
        );
        return new ConversionResult.Rejected(ConversionRejections.TooManyTokens(request.MaxEstimatedTokens));
    }

    // ZipArchive never reads past an entry's declared size, even when the size is
    // forged, so the declared sizes bound what both this converter and
    // MarkItDotNet can unpack.
    private static bool ExceedsMaxUncompressedBytes(MemoryStream docx, long maxUncompressedBytes)
    {
        docx.Position = 0;
        using var archive = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);

        // Summed with an early exit, so huge declared sizes cannot overflow the total
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            totalBytes += entry.Length;
            if (totalBytes > maxUncompressedBytes)
            {
                return true;
            }
        }

        return false;
    }

    // Rewrites the Word XML parts in place and leaves the stream at the start
    private static async Task NormalizeDocx(MemoryStream docx, CancellationToken stoppingToken)
    {
        docx.Position = 0;

        await using (var archive = new ZipArchive(docx, ZipArchiveMode.Update, leaveOpen: true))
        {
            await NormalizeEntries(archive, stoppingToken);
        }

        docx.Position = 0;
    }

    private static async Task NormalizeEntries(ZipArchive archive, CancellationToken stoppingToken)
    {
        // find all XML documents in word folder inside archive
        var entriesToUpdate = archive.Entries
            .Where(entry => entry.FullName.EndsWith("word/document.xml", StringComparison.InvariantCultureIgnoreCase) || entry.FullName.EndsWith("word/styles.xml", StringComparison.InvariantCultureIgnoreCase))
            .ToList();

        foreach (var entry in entriesToUpdate)
        {
            XDocument part;
            await using (var stream = await entry.OpenAsync(stoppingToken))
            {
                part = await XDocument.LoadAsync(stream, LoadOptions.None, stoppingToken);
            }

            // Unchanged parts are left alone, since XDocument never round-trips to the same text
            if (!DocxXmlNormalizer.Normalize(part))
            {
                continue;
            }

            // Remove the old XML entry from the archive and save the new entry in the archive
            var fullName = entry.FullName;
            entry.Delete();

            var newEntry = archive.CreateEntry(fullName);
            await using var newStream = await newEntry.OpenAsync(stoppingToken);
            await part.SaveAsync(newStream, SaveOptions.DisableFormatting, stoppingToken);
        }
    }
}

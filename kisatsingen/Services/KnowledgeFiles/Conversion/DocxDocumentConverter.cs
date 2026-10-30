using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ElBruno.MarkItDotNet;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// NOTE: This docx document converter is not perfect. It most likely will get some syntax wrong,
// but the overall gist is preserved and understandable for further processing.
public class DocxDocumentConverter : IDocumentConverter
{
    private static readonly string[] QuoteStyles = ["Sitat", "Sterksitat", "Sterktsitat", "Sterk sitat", "Sterkt sitat"];

    // Values that switch off a Word toggle property, as in <w:strike w:val="0"/>
    private static readonly string[] SwitchedOffValues = ["0", "false", "off"];

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
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to normalize docx document '{Filename}' with ContentType '{ContentType}'",
                request.FileName,
                request.ContentType
            );
            return new ConversionResult.Rejected(ConversionRejections.ErrorParsingDocx);
        }

        var result = await _markdownService.ConvertAsync(docx, Path.GetExtension(request.FileName), ct);

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
            .Where(entry => entry.FullName.StartsWith("word/", StringComparison.InvariantCultureIgnoreCase) && entry.FullName.EndsWith(".xml", StringComparison.InvariantCultureIgnoreCase))
            .ToList();

        foreach (var entry in entriesToUpdate)
        {
            string xmlContent;
            using (var reader = new StreamReader(await entry.OpenAsync(stoppingToken), Encoding.UTF8))
            {
                xmlContent = await reader.ReadToEndAsync(stoppingToken);
            }

            // normalize localized styles
            var normalizedXml = xmlContent
                // Headings (pStyle -> Heading*)
                .Replace("val=\"Overskrift1\"", "val=\"Heading1\"")
                .Replace("val=\"Overskrift2\"", "val=\"Heading2\"")
                .Replace("val=\"Overskrift3\"", "val=\"Heading3\"")
                .Replace("val=\"Overskrift4\"", "val=\"Heading4\"")
                .Replace("val=\"Overskrift5\"", "val=\"Heading5\"")
                .Replace("val=\"Overskrift6\"", "val=\"Heading6\"")
                .Replace("val=\"Overskrift7\"", "val=\"Heading6\"")
                .Replace("val=\"Overskrift8\"", "val=\"Heading6\"")
                .Replace("val=\"Overskrift9\"", "val=\"Heading6\"")
                .Replace("val=\"Overskrift10\"", "val=\"Heading6\"")

                //.Replace("val=\"Listeavsnitt\"", "val=\"Heading6\"") // NOTE: There's no actual replacement for this in Markdown. CAUTION: Enabling this will also replace all list items
                .Replace("val=\"Tittel\"", "val=\"Heading1\"")
                .Replace("val=\"Undertittel\"", "val=\"Heading2\"")

                // Italic and Bold (rStyle -> b or i or a combination)
                .Replace("<w:rStyle w:val=\"Sterk\"/>", "<w:b />")
                .Replace("<w:rStyle w:val=\"Sterk\" />", "<w:b />")
                .Replace("<w:rStyle w:val=\"Sterkreferanse\"/>", "<w:b />")
                .Replace("<w:rStyle w:val=\"Sterk referanse\"/>", "<w:b />")
                .Replace("<w:rStyle w:val=\"Utheving\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Utheving\" />", "<w:i />")
                .Replace("<w:rStyle w:val=\"Svakutheving\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Svak utheving\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Boktittel\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Svakreferanse\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Svak referanse\"/>", "<w:i />")
                .Replace("<w:rStyle w:val=\"Sterkutheving\"/>", "<w:b /><w:i />")
                .Replace("<w:rStyle w:val=\"Sterk utheving\"/>", "<w:b /><w:i />");

            // Quotes and strikethrough have no style MarkItDotNet understands, so their markers are written into the text itself
            var xDoc = XDocument.Parse(normalizedXml);
            var isParagraphChanged = false;

            foreach (var paragraph in xDoc.Descendants().Where(element => element.Name.LocalName == "p").ToList())
            {
                // Strikethrough first, so a struck quote becomes "> *~~text~~*" and not "~~> *text*~~"
                var isStruck = NormalizeStrikethrough(paragraph);
                var isQuote = NormalizeQuote(paragraph);
                isParagraphChanged = isParagraphChanged || isStruck || isQuote;
            }

            // Unchanged parts are left alone. Decided before serializing, since XDocument drops the XML declaration and never round-trips to the same text
            if (normalizedXml == xmlContent && !isParagraphChanged)
            {
                continue;
            }

            normalizedXml = xDoc.ToString(SaveOptions.DisableFormatting);

            // Remove the old XML entry from the archive and save the new entry in the archive
            var fullName = entry.FullName;
            entry.Delete();

            var newEntry = archive.CreateEntry(fullName);
            await using var writer = new StreamWriter(await newEntry.OpenAsync(stoppingToken), Encoding.UTF8);
            await writer.WriteAsync(normalizedXml);
        }
    }

    private static bool NormalizeStrikethrough(XElement paragraph)
    {
        // Word marks a fully struck paragraph on its paragraph mark. Other run properties, like the language, say nothing about strikethrough.
        var paragraphMarkStrike = FindChild(FindChild(FindChild(paragraph, "pPr"), "rPr"), "strike");
        if (paragraphMarkStrike is null || !IsSwitchedOn(paragraphMarkStrike))
        {
            return false;
        }

        // Only the strike elements go, so bold, italic and other formatting survive
        foreach (var strike in paragraph.Descendants().Where(element => element.Name.LocalName == "strike").ToList())
        {
            strike.Remove();
        }

        WrapText(paragraph, "~~", "~~");
        return true;
    }

    private static bool NormalizeQuote(XElement paragraph)
    {
        var pStyle = FindChild(FindChild(paragraph, "pPr"), "pStyle");
        var styleName = pStyle?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "val")?.Value;
        if (pStyle is null || styleName is null || !QuoteStyles.Contains(styleName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var emphasis = styleName.Contains("sterk", StringComparison.OrdinalIgnoreCase) ? "**" : "*";

        // Remove the pStyle element so MarkItDotNet treats this as regular text
        pStyle.Remove();

        WrapText(paragraph, "> " + emphasis, emphasis);
        return true;
    }

    // Word splits a paragraph into runs at every edit or spell check, so the markers go
    // before the first text and after the last text of the paragraph, not of one run
    private static void WrapText(XElement paragraph, string prefix, string suffix)
    {
        var textNodes = paragraph.Descendants().Where(element => element.Name.LocalName == "t").ToList();
        if (textNodes.Count == 0)
        {
            return;
        }

        textNodes[0].Value = prefix + textNodes[0].Value;
        textNodes[^1].Value += suffix;
    }

    private static bool IsSwitchedOn(XElement toggle)
    {
        var value = toggle.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "val")?.Value;
        return value is null || !SwitchedOffValues.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static XElement? FindChild(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
}

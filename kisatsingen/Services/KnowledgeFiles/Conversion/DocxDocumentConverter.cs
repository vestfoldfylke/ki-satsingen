using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

public class DocxDocumentConverter : IDocumentConverter
{
    // Images are already compressed and unpack to about their own size. XML packs
    // 10-20x, but four uploads' worth of it holds far more text than the token cap
    // allows, so only zip bombs and documents that would be refused anyway are
    // stopped. Tied to the upload limit so the two cannot drift apart.
    private const int MaxUncompressedToUploadRatio = 4;

    // The Markdown holds all the document's text plus markers, so writing
    // less than this share of it means the converter lost text it can't read.
    private const double MinWrittenToSourceTextRatio = 0.9;

    private readonly ILogger<DocxDocumentConverter> _logger;
    private readonly long _maxUncompressedBytes;

    public IReadOnlyList<string> ContentTypes { get; } = [AttachmentContentTypes.Docx.ContentType];

    public DocxDocumentConverter(ILogger<DocxDocumentConverter> logger, AttachmentOptions attachmentOptions)
    {
        _logger = logger;
        _maxUncompressedBytes = attachmentOptions.MaxFileBytes * MaxUncompressedToUploadRatio;
    }

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct) => Task.FromResult(Convert(request, ct));

    private ConversionResult Convert(ConversionRequest request, CancellationToken ct)
    {
        var limits = new OfficeLimits(_maxUncompressedBytes, TokenEstimate.MaxCharacters(request.MaxEstimatedTokens), request.MaxEstimatedTokens);
        var conversionResult = DocxConverter.Convert(request.Content, limits, ct);

        if (conversionResult.Success)
        {
            if (conversionResult.Markdown.Length == 0)
            {
                return new ConversionResult.Rejected(ConversionRejections.EmptyDocx);
            }

            WarnWhenTextWasLost(request, conversionResult);
            return new ConversionResult.Converted(conversionResult.Markdown, ContentOrigin.Docx, null);
        }

        if (conversionResult.ErrorType == OfficeConversionResultErrorType.TooLarge)
        {
            _logger.LogWarning(
                "Docx document '{Filename}' with ContentType '{ContentType}' unpacks to more than {MaxUncompressedBytes} bytes.",
                request.FileName,
                request.ContentType,
                _maxUncompressedBytes
            );

            return new ConversionResult.Rejected(conversionResult.Error ?? ConversionRejections.DocxTooLargeUncompressed);
        }

        if (conversionResult.ErrorType == OfficeConversionResultErrorType.TooLong)
        {
            _logger.LogWarning(
                "Converting docx document '{Filename}' with ContentType '{ContentType}' to Markdown would exceed the limit for MaxEstimatedTokens ({MaxEstimatedTokens}).",
                request.FileName,
                request.ContentType,
                request.MaxEstimatedTokens
            );

            return new ConversionResult.Rejected(conversionResult.Error ?? ConversionRejections.TooManyTokens(request.MaxEstimatedTokens));
        }

        _logger.LogWarning(
            conversionResult.Exception,
            "Docx document '{Filename}' with ContentType '{ContentType}' is unreadable.",
            request.FileName,
            request.ContentType
        );

        return new ConversionResult.Rejected(conversionResult.Error ?? ConversionRejections.ErrorParsingDocx);
    }

    // The file is still accepted: this is how documents the converter doesn't
    // handle are found, since nobody notices text that is silently missing.
    private void WarnWhenTextWasLost(ConversionRequest request, OfficeConversionResult conversionResult)
    {
        if (conversionResult.WrittenTextCharacters >= conversionResult.SourceTextCharacters * MinWrittenToSourceTextRatio)
        {
            return;
        }

        _logger.LogWarning(
            "Converting docx document '{Filename}' wrote {WrittenTextCharacters} of the document's {SourceTextCharacters} text characters. Find which elements the converter skips, using a copy of the document without sensitive content.",
            request.FileName,
            conversionResult.WrittenTextCharacters,
            conversionResult.SourceTextCharacters
        );
    }
}

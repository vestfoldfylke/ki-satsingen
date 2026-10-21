namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Exactly one converter per content type, chosen by DocumentConverterRegistry.
// A new converter also needs its registration in Program.cs and its
// extensions in AttachmentContentTypes.
public interface IDocumentConverter
{
    IReadOnlyList<string> ContentTypes { get; }

    // IMPORTANT: Must stop as fast as possible when ct is cancelled. The
    // pipeline waits for conversion to end, so a converter that ignores ct holds
    // its turn, the user's Stop and a conversion slot until it finishes.
    // CA2016 only checks that ct is passed on, not that it is honoured.
    // The AttachmentUploader refuses files exceeding the maximum file size, and
    // the KnowledgeFilePipeline handles timeouts, so converters don't need to
    // check for these.
    // Converters should check request.MaxEstimatedTokens during conversion, for
    // early rejections of too large text-content. The KnowledgeFilePipeline also
    // checks the final result against MaxEstimatedTokens.
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct);
}

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
    // The KnowledgeFilePipeline handles timeouts and files exceeding the maximum
    // file size, so converters don't need to check for these.
    // Converters should, however, check request.MaxEstimatedTokens themselves,
    // since the token estimate depends on the extracted content and can only be
    // calculated during conversion.
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct);
}

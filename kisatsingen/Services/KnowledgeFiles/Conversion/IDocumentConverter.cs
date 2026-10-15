namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// One implementation per content type, chosen by DocumentConverterRegistry.
// Moving a type to a remote service means registering a converter that calls
// that service instead.
public interface IDocumentConverter
{
    IReadOnlyList<string> ContentTypes { get; }

    // IMPORTANT: Must stop as fast as possible when ct is cancelled. The
    // pipeline waits for conversion to end, so a converter that ignores ct holds
    // its turn, the user's Stop and a conversion slot until it finishes.
    // CA2016 only checks that ct is passed on, not that it is honoured.
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct);
}

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// One implementation per content type, chosen by DocumentConverterRegistry.
// Moving a type to a remote service means registering a converter that calls
// that service instead.
public interface IDocumentConverter
{
    IReadOnlyList<string> ContentTypes { get; }

    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct);
}

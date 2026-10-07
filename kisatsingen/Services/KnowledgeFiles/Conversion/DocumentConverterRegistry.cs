using System.Diagnostics.CodeAnalysis;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Which converter handles which content type is decided by what is registered
// in DI, so moving a type elsewhere is a registration change.
public sealed class DocumentConverterRegistry
{
    private readonly Dictionary<string, IDocumentConverter> _byContentType = new(StringComparer.OrdinalIgnoreCase);

    public DocumentConverterRegistry(IEnumerable<IDocumentConverter> converters)
    {
        foreach (var converter in converters)
        {
            foreach (var contentType in converter.ContentTypes)
            {
                if (!_byContentType.TryAdd(contentType, converter))
                {
                    throw new InvalidOperationException(
                        $"Both {_byContentType[contentType].GetType().Name} and {converter.GetType().Name} are registered for '{contentType}'. Register exactly one converter per content type.");
                }
            }
        }
    }

    public bool TryGet(string contentType, [MaybeNullWhen(false)] out IDocumentConverter converter) =>
        _byContentType.TryGetValue(contentType, out converter);
}

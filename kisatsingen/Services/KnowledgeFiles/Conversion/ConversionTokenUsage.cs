using kisatsingen.Services.Chat;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// What a converter that calls a model spent, with the provider and model it is
// priced by.
public sealed record ConversionTokenUsage(string Provider, string ModelId, MessageUsage Usage);

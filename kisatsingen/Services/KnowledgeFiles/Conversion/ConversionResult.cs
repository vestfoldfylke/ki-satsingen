namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Plain data only, so a converter running in another service can return the
// same thing as JSON.
//
// TokenUsage is on every result, since a rejected answer can cost as much as a
// converted one. Null for a converter that calls no model.
public abstract record ConversionResult(ConversionTokenUsage? TokenUsage)
{
    public sealed record Converted(string Markdown, ContentOrigin Origin, int? PageCount, ConversionTokenUsage? TokenUsage = null)
        : ConversionResult(TokenUsage);

    // Shown to the user as is.
    public sealed record Rejected(string Reason, ConversionTokenUsage? TokenUsage = null)
        : ConversionResult(TokenUsage);
}

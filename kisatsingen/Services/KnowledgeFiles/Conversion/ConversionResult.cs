namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Plain data only, so a converter running in another service can return the
// same thing as JSON.
public abstract record ConversionResult
{
    public sealed record Converted(string Markdown, ContentOrigin Origin, int? PageCount) : ConversionResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : ConversionResult;
}

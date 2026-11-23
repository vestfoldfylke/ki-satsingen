namespace kisatsingen.AIFunctions.WebSearch;

// What the model receives, serialised in camelCase with null fields left out.
// Model behaviour depends on these shapes, so they are pinned by tests.
internal static class SearchWebResults
{
    // Content last: it is the long part, and everything the model needs to
    // judge it comes before.
    public sealed record SearchWebResult(
        IReadOnlyList<string> Queries,
        IReadOnlyList<WebSource> Sources,
        string ContentNotice,
        string Content);

    public sealed record WebSource(string Url, string? Title);
}

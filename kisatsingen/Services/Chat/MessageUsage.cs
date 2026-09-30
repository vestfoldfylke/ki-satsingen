namespace kisatsingen.Services.Chat;

// Reported is what the provider said; Estimated covers only the round trips it never
// reported on (a stop or failure mid-round), so the two add up rather than overlap.
//
// No provider total: it is input plus output on every provider here, and a second
// source for the same number is one that can disagree with the parts shown beside it.
public sealed record MessageUsage(
    long? InputTokens,
    long? OutputTokens,
    long? EstimatedInputTokens = null,
    long? EstimatedOutputTokens = null)
{
    public bool IsEstimated => EstimatedInputTokens > 0 || EstimatedOutputTokens > 0;

    public long? CombinedInputTokens => SumOfKnown(InputTokens, EstimatedInputTokens);

    public long? CombinedOutputTokens => SumOfKnown(OutputTokens, EstimatedOutputTokens);

    public long? CombinedTotalTokens => SumOfKnown(CombinedInputTokens, CombinedOutputTokens);

    private bool HasAnyCount =>
        new[] { InputTokens, OutputTokens, EstimatedInputTokens, EstimatedOutputTokens }.Any(count => count is not null);

    public static MessageUsage? FromCounts(
        long? inputTokens,
        long? outputTokens,
        long? estimatedInputTokens,
        long? estimatedOutputTokens)
    {
        var usage = new MessageUsage(inputTokens, outputTokens, estimatedInputTokens, estimatedOutputTokens);
        return usage.HasAnyCount ? usage : null;
    }

    // Null when none is known, so a count nobody reported never shows as 0.
    private static long? SumOfKnown(params long?[] counts) =>
        counts.All(count => count is null) ? null : counts.Sum();
}

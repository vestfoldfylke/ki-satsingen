namespace kisatsingen.Data.Entities;

public record TokenUsageSummary(
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalEstimatedInputTokens,
    long TotalEstimatedOutputTokens,
    long TotalCachedInputTokens,
    long TotalReasoningTokens,
    int TurnCount)
{
    public static TokenUsageSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

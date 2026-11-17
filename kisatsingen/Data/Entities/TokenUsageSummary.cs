namespace kisatsingen.Data.Entities;

public record TokenUsageSummary(
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalEstimatedInputTokens,
    long TotalEstimatedOutputTokens,
    long TotalCachedInputTokens,
    long TotalReasoningTokens,
    long TotalConversionTokens,
    int TurnCount)
{
    public static TokenUsageSummary Empty { get; } = new(
        TotalInputTokens: 0,
        TotalOutputTokens: 0,
        TotalEstimatedInputTokens: 0,
        TotalEstimatedOutputTokens: 0,
        TotalCachedInputTokens: 0,
        TotalReasoningTokens: 0,
        TotalConversionTokens: 0,
        TurnCount: 0);
}

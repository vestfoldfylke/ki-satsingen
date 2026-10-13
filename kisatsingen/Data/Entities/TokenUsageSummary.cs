namespace kisatsingen.Data.Entities;

public record TokenUsageSummary(
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalEstimatedInputTokens,
    long TotalEstimatedOutputTokens,
    int TurnCount);
namespace kisatsingen.Data.Entities;

public sealed record UsageByKey<TKey>(
    TKey Key,
    long InputTokens,
    long OutputTokens,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    int TurnCount);

namespace kisatsingen.Data.Entities;

public sealed record UsageTimeBucket(
    DateTimeOffset Bucket,
    long InputTokens,
    long OutputTokens,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    int TurnCount);

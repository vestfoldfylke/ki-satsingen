namespace kisatsingen.Data.Entities;

public sealed record ResponseMeta(
    string? ResponseId,
    string? ModelId,
    string? FinishReason,
    int UpdateCount,
    int CharCount,
    long DurationMs,
    long? TimeToFirstTokenMs,
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens);
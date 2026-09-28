namespace kisatsingen.Services.Chat;

public sealed record TurnMetadata(
    string? ServedModelId,
    string? ResponseId,
    string? FinishReason,
    MessageUsage? Usage,
    long? DurationMs,
    long? TimeToFirstTokenMs);

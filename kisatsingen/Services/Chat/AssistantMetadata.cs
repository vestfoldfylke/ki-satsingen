namespace kisatsingen.Services.Chat;

public sealed record AssistantMetadata(
    string? ModelId,
    string? ResponseId,
    string? FinishReason,
    MessageUsage? Usage,
    long? DurationMs,
    long? TimeToFirstTokenMs,
    DateTimeOffset? CreatedAt,
    string? SystemPrompt);

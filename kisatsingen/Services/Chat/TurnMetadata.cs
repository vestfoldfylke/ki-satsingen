namespace kisatsingen.Services.Chat;

// What the provider reported about one assistant turn. Named for the turn, not
// the assistant: Data.Entities.Assistant is a configured persona, and a record
// called AssistantMetadata sitting next to it would read as that entity's
// metadata rather than one response's.
public sealed record TurnMetadata(
    string? ModelId,
    string? ResponseId,
    string? FinishReason,
    MessageUsage? Usage,
    long? DurationMs,
    long? TimeToFirstTokenMs,
    DateTimeOffset? CreatedAt,
    string? SystemPrompt);

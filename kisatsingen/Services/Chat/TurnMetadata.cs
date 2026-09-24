namespace kisatsingen.Services.Chat;

// Named for the turn, not "Assistant", which here means a configured persona.
//
// ModelDisplayName is resolved when the entry is built, not at render time, so a
// model removed from the catalogue keeps its name in past chats.
public sealed record TurnMetadata(
    string? ModelId,
    ChatModelKey? ModelKey,
    string? ModelDisplayName,
    string? ResponseId,
    string? FinishReason,
    MessageUsage? Usage,
    long? DurationMs,
    long? TimeToFirstTokenMs,
    DateTimeOffset? CreatedAt,
    string? SystemPrompt);

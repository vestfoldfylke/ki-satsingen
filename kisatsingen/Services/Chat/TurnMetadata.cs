namespace kisatsingen.Services.Chat;

// What the provider reported about one assistant turn. Named for the turn, not
// the assistant: Data.Entities.Assistant is a configured persona, and a record
// called AssistantMetadata sitting next to it would read as that entity's
// metadata rather than one response's.
// ModelKey and ModelDisplayName are ours, ModelId is the provider's. All three
// are here because they answer different questions: the key is what the
// transcript compares to find a model boundary, the display name is what it
// shows, and the id is what actually served the turn.
//
// The display name is resolved once, when the entry is built, rather than looked
// up at render time — a model dropped from the catalogue would otherwise lose its
// name in every past chat that used it.
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

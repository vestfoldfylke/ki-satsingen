namespace kisatsingen.Services.Chat;

// What the provider reported about a turn, plus how long it took. Null on a turn
// that ended before the model was asked anything.
public sealed record TurnMetadata(
    string? ServedModelId,
    string? ResponseId,
    string? FinishReason,
    MessageUsage? Usage,
    long? DurationMs,
    long? TimeToFirstTokenMs);

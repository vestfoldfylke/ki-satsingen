namespace kisatsingen.Services.Chat;

// Our own stable identifier for a catalogued model — "fast", never
// "gpt-4o-mini-2024-07-18".
//
// Wrapped rather than left a bare string because three different model strings
// pass through this code and all three are interchangeable to the compiler:
//
//   ChatModelKey        ours, stable, persisted            "fast"
//   ChatModel.ModelId   the id we ask the provider for     "gpt-4o-mini"
//   ChatMessage.ModelId the id the provider says it served "gpt-4o-mini-2024-07-18"
//
// Persisted, so the value has to outlive every rename of the model behind it.
// Changing one silently orphans the stored rows that carry it.
public readonly record struct ChatModelKey(string Value)
{
    // Validated in the ctor: a persisted identifier that silently accepts null
    // or whitespace poisons every dictionary lookup and log line it touches.
    // `default(ChatModelKey)` still bypasses this — that is a C# language
    // guarantee we cannot remove — so any code that could produce one (an
    // uninitialised field, a JSON deserialisation with a missing property)
    // still has to reject it explicitly at its own boundary.
    public string Value { get; } = ValidateOrThrow(Value);

    // For readers of stored data, where a key that is null, empty or whitespace is
    // a row that simply does not name a model — not a programming error. The
    // column is nullable with no non-empty constraint, so an empty string is
    // reachable, and the ctor's throw would turn one bad row into a chat that can
    // never be opened again. Callers that construct a key from their own literals
    // keep using the ctor: there, a blank really is a bug.
    public static ChatModelKey? TryCreate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new ChatModelKey(value);

    public override string ToString() => Value;

    private static string ValidateOrThrow(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}

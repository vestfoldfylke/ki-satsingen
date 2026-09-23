namespace kisatsingen.Services.Chat;

// Wrapped because three model strings flow through this code and the compiler
// can't tell them apart:
//
//   ChatModelKey         ours, persisted                   "fast"
//   ChatModel.ModelId    what we ask the provider for      "gpt-4o-mini"
//   ChatMessage.ModelId  what the provider says it served  "gpt-4o-mini-2024-07-18"
//
// Persisted: renaming a key orphans every stored row that carries it.
public readonly record struct ChatModelKey(string Value)
{
    // default(ChatModelKey) bypasses this check, so any boundary that could
    // produce one must reject it itself.
    public string Value { get; } = ValidateOrThrow(Value);

    // For stored rows, where a blank key means "no model" rather than a bug. The
    // ctor's throw would let one bad row make a chat impossible to open.
    public static ChatModelKey? TryCreate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new ChatModelKey(value);

    public override string ToString() => Value;

    private static string ValidateOrThrow(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}

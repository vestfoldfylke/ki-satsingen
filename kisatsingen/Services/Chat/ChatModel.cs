namespace kisatsingen.Services.Chat;

// Safe to render: holds no client, so the picker never touches a provider
// connection (ChatModelRuntime has that). Named properties, because eight
// positional literals at a registration site are unreadable.
public sealed record ChatModel
{
    public required ChatModelKey Key { get; init; }

    // Free to change: nothing is keyed on it.
    public required string DisplayName { get; init; }

    public required string ShortDescription { get; init; }

    public required string LongDescription { get; init; }

    public required string Provider { get; init; }

    // Applied only when the client is built, never also as ChatOptions.ModelId;
    // see ChatModelServiceCollectionExtensions.OpenAiCompatible.
    public required string ModelId { get; init; }

    public required int ContextWindowTokens { get; init; }

    public required string IconName { get; init; }

    // The estimate runs high, so this fires a little before the window is full —
    // the useful direction. Null means nothing to send, never "too big".
    public bool WouldOverflow(long? estimatedContextTokens) =>
        estimatedContextTokens is { } tokens && tokens > ContextWindowTokens;
}

namespace kisatsingen.Services.Chat;

// Everything about a catalogued model that is safe to render. Deliberately holds
// no client: the component that draws the picker has no business holding a live
// provider connection. ChatModelRuntime is the other half.
//
// Named properties rather than a positional record — eight positional arguments
// at a registration site is a row of string literals nobody can read.
public sealed record ChatModel
{
    public required ChatModelKey Key { get; init; }

    // What the picker shows. Free to change: nothing is keyed on it, and past
    // chats record their own copy in the switch event's detail.
    public required string DisplayName { get; init; }

    // One line, shown under the name in the dropdown.
    public required string ShortDescription { get; init; }

    // The longer "when would I pick this one" text.
    public required string LongDescription { get; init; }

    public required string Provider { get; init; }

    // The id sent on the wire. Set when the client is built and never also placed
    // on ChatOptions.ModelId — verified against Microsoft.Extensions.AI.OpenAI
    // 10.9.0: when both are present the options value silently wins, so there is
    // exactly one place this may be applied.
    public required string ModelId { get; init; }

    public required int ContextWindowTokens { get; init; }

    public required string IconName { get; init; }

    // Whether a conversation of this size is already too big for this model.
    //
    // The size is an estimate, not a measurement, and it is biased to run high —
    // see ContextTokenEstimator. So this fires somewhat before the window is
    // genuinely full, which is the useful direction: a warning that arrives after
    // the request has already failed is not a warning.
    //
    // Null means there is nothing to send yet, and unknown must never read as "too
    // big". A warning nobody can act on is worse than no warning.
    public bool WouldOverflow(long? estimatedContextTokens) =>
        estimatedContextTokens is { } tokens && tokens > ContextWindowTokens;
}

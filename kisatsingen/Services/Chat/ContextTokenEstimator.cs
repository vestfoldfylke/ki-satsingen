using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// How big the next request will be, counted from the request itself rather than
// from what a provider said about the last one.
//
// Deliberately crude, and deliberately not a tokenizer. A real one would be exact
// for a single provider and confidently wrong for the other — GPT and Mistral do
// not share a vocabulary — so half the numbers would be false precision, bought
// with a dependency neither provider fully justifies.
//
// Pure, so the arithmetic below is a unit test rather than something only
// observable through a live conversation.
internal static class ContextTokenEstimator
{
    // The familiar "about four characters" is an English figure. Norwegian runs
    // denser on both providers' vocabularies: æ, ø and å are frequently their own
    // tokens, and compounds fragment where English would have separate words.
    // Three is chosen low on purpose — a low divisor estimates high, and this
    // number drives a warning, where firing early is recoverable and firing late
    // is not.
    private const int TokenLengthInCharacters = 3;

    // Each message costs its role and the format's delimiters on top of its text.
    // The exact figure is provider-specific and small; this keeps a conversation
    // of many short messages from estimating at nearly nothing.
    private const int TokensPerMessage = 4;

    // The system prompt is counted even though it travels as
    // ChatOptions.Instructions rather than in the message list: the provider puts
    // it in the request either way, so it occupies the context window either way.
    public static long Estimate(IReadOnlyList<ChatMessage> messages, string? systemPrompt)
    {
        long characters = 0;
        long messageCount = messages.Count;

        foreach (var message in messages)
        {
            characters += CountCharacters(message);
        }

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            characters += systemPrompt.Length;
            messageCount++;
        }

        return (characters / TokenLengthInCharacters) + (messageCount * TokensPerMessage);
    }

    private static long CountCharacters(ChatMessage message)
    {
        long characters = 0;

        foreach (var content in message.Contents)
        {
            characters += content switch
            {
                TextContent text => text.Text?.Length ?? 0,
                FunctionCallContent call => CountCharacters(call),

                // A tool result is often the largest thing in a conversation, and
                // missing it is how an estimate comes to understate the one case
                // that actually overflows.
                FunctionResultContent result => result.Result?.ToString()?.Length ?? 0,

                // Images and raw data have no character count worth guessing at.
                // Counting them as nothing understates; a made-up constant would
                // be wrong in a way that looks deliberate. Revisit when this app
                // can send them.
                _ => 0
            };
        }

        return characters;
    }

    // ToString rather than JsonSerializer: these values are already JsonElement on
    // every path that reaches here, so ToString is the JSON text, and an estimate
    // does not justify re-serialising the whole transcript on every render just to
    // measure its length.
    private static long CountCharacters(FunctionCallContent call)
    {
        long characters = call.Name.Length;

        if (call.Arguments is null)
        {
            return characters;
        }

        foreach (var (name, value) in call.Arguments)
        {
            characters += name.Length + (value?.ToString()?.Length ?? 0);
        }

        return characters;
    }
}

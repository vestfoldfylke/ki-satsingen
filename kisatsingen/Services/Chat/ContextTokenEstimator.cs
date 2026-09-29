using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// What a request would cost in the context window: its text through
// TokenEstimate, plus what a conversation adds on top of text.
internal static class ContextTokenEstimator
{
    // Role and delimiter overhead, so many short messages don't estimate at nothing.
    private const int TokensPerMessage = 4;

    // The system prompt travels as Instructions, not in the list, but still
    // occupies the window.
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

        return TokenEstimate.FromCharacters(characters) + (messageCount * TokensPerMessage);
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
                // Often the largest thing in the conversation; missing it understates
                // exactly the chats that overflow.
                FunctionResultContent result => result.Result?.ToString()?.Length ?? 0,
                // Images and data have no honest character count. Revisit when the app can send them.
                _ => 0
            };
        }

        return characters;
    }

    // ToString, not JsonSerializer: the values are JsonElement already, and an
    // estimate doesn't justify re-serialising the transcript on every render.
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

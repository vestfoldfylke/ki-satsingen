using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// What a request would cost in the context window: its text through
// TokenEstimate, plus what a conversation adds on top of text.
internal static class ContextTokenEstimator
{
    // English runs about four characters per token; Norwegian is denser on both
    // providers (æ, ø, å and split compounds). Deliberately low so the estimate runs
    // high: this drives a warning, and warning early is the recoverable mistake.
    // Unreported usage is estimated here too and overstated the same way, which is
    // why it is stored apart from reported usage.
    private const int TokenLengthInCharacters = 3;

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

    // Both below count an unknown segment kind as nothing rather than throwing: they
    // run while a turn ends, where a throw would lose the turn over an estimate.

    // What the model wrote: a tool's result came from us, not from it.
    public static long EstimateGenerated(TurnSegment segment) => segment switch
    {
        TextSegment text => text.Text.Length / TokenLengthInCharacters,
        ToolSegment tool => CountCall(tool) / TokenLengthInCharacters,
        _ => 0
    };

    public static long EstimateResent(TurnSegment segment) => TokensPerMessage + segment switch
    {
        TextSegment text => text.Text.Length / TokenLengthInCharacters,
        ToolSegment tool => (CountCall(tool) + (tool.Result?.Length ?? 0)) / TokenLengthInCharacters,
        _ => 0
    };

    private static long CountCall(ToolSegment tool) => tool.ToolName.Length + tool.Arguments.GetRawText().Length;

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

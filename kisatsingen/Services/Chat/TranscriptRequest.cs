using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// Pure, so "events are never sent to the model" is a unit test, not a habit.
//
// The system prompt is not in the list: it travels as ChatOptions.Instructions
// and each adapter places it where its wire format wants it (a system message
// for OpenAI, a top-level parameter for Anthropic).
internal static class TranscriptRequest
{
    public static List<ChatMessage> Build(IReadOnlyList<TranscriptEntry> entries)
    {
        var request = new List<ChatMessage>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry is MessageEntry message)
            {
                request.Add(message.Message);
            }
        }

        return request;
    }
}

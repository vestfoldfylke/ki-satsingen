using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// The model's view of the transcript. Pure, so the rule that matters most here —
// events are never sent to the model — is a unit test rather than a code review
// habit.
internal static class TranscriptRequest
{
    public static List<ChatMessage> Build(IReadOnlyList<TranscriptEntry> entries, string systemPrompt)
    {
        var request = new List<ChatMessage>(entries.Count + 1)
        {
            new(ChatRole.System, systemPrompt)
        };

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

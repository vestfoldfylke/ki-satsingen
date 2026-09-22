using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// The model's view of the transcript. Pure, so the rule that matters most here —
// events are never sent to the model — is a unit test rather than a code review
// habit.
//
// The system prompt is deliberately not in this list. It travels as
// ChatOptions.Instructions and the provider adapter places it where its own wire
// format wants it: on chat completions that is a system message at position 0,
// byte for byte what this used to build by hand, but Anthropic takes it as a
// top-level parameter instead. Letting the adapter decide is what keeps this
// list portable across providers.
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

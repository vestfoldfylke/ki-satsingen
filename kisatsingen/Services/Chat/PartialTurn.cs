using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// A turn that stopped mid-flight, reduced to what is safe to keep.
//
// The danger this exists for: stopping during a tool call leaves an assistant
// message holding a FunctionCallContent that nothing ever answered. Stored, it
// goes back out with every later request in that chat, and providers reject a
// tool call with no matching result — so keeping the partial naively would not
// cost the user a half-answer, it would brick the conversation permanently.
// Dropping the unanswered call is what makes persisting partials safe at all.
//
// Pure, so that rule is a unit test rather than a property of a cancelled turn
// nobody can reproduce on demand.
internal static class PartialTurn
{
    // Null when nothing worth storing survives — a turn stopped before the model
    // said anything, or one whose only content was an unanswered tool call.
    public static ChatResponse? Prune(ChatResponse response)
    {
        var answeredCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent result)
                {
                    answeredCallIds.Add(result.CallId);
                }
            }
        }

        var kept = new List<ChatMessage>(response.Messages.Count);
        foreach (var message in response.Messages)
        {
            var contents = message.Contents
                .Where(content => content is not FunctionCallContent call || answeredCallIds.Contains(call.CallId))
                .ToList();

            // A message left with nothing, or with nothing but blank text, is the
            // shape an empty streaming chunk degrades to. Storing it would put a
            // silent assistant bubble in the transcript.
            if (contents.Count == 0 || contents.TrueForAll(IsBlankText))
            {
                continue;
            }

            kept.Add(new ChatMessage(message.Role, contents));
        }

        if (kept.Count == 0)
        {
            return null;
        }

        return new ChatResponse(kept)
        {
            ModelId = response.ModelId,
            ResponseId = response.ResponseId,
            FinishReason = response.FinishReason,
            Usage = response.Usage
        };
    }

    private static bool IsBlankText(AIContent content) =>
        content is TextContent text && string.IsNullOrWhiteSpace(text.Text);
}

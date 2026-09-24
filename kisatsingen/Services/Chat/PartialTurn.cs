using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// Whatever a stopped turn stores goes out again with every later request, so a
// partial in a shape the provider rejects leaves the chat unable to send ever
// again. Two shapes do that:
//
//   an unanswered tool call — providers reject a call with no result;
//   history ending on a tool result — the next user message would follow the
//   tool message directly, which Mistral rejects ("Unexpected role 'user' after
//   role 'tool'"). OpenAI accepts it.
//
// Cutting after the last assistant text loses nothing the user saw: only text
// streams to the screen.
internal static class PartialTurn
{
    // Null when the model never produced any text.
    public static ChatResponse? Prune(ChatResponse response)
    {
        var lastAnswerIndex = FindLastAnswerIndex(response.Messages);
        if (lastAnswerIndex < 0)
        {
            return null;
        }

        var kept = response.Messages.Take(lastAnswerIndex + 1).ToList();

        // Over the kept range only: a result after the cut is never sent.
        var answeredCallIds = kept
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);

        var pruned = new List<ChatMessage>(kept.Count);
        foreach (var message in kept)
        {
            var contents = message.Contents
                .Where(content => content is not FunctionCallContent call || answeredCallIds.Contains(call.CallId))
                .ToList();

            // Empty streaming chunks degrade to this; stored, they render as a blank bubble.
            if (contents.Count == 0 || contents.TrueForAll(IsBlankText))
            {
                continue;
            }

            pruned.Add(new ChatMessage(message.Role, contents));
        }

        return new ChatResponse(pruned)
        {
            ModelId = response.ModelId,
            ResponseId = response.ResponseId,
            FinishReason = response.FinishReason,
            Usage = response.Usage
        };
    }

    private static int FindLastAnswerIndex(IList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(messages[index].Text))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsBlankText(AIContent content) =>
        content is TextContent text && string.IsNullOrWhiteSpace(text.Text);
}

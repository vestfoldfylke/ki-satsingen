using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// A turn that stopped mid-flight, reduced to what is safe to keep.
//
// Whatever is stored goes back out with every later request in the chat, so a
// partial that leaves the history in a shape a provider rejects does not cost
// the user a half-answer — it leaves the conversation permanently unable to
// send. Two shapes do that:
//
//   An unanswered tool call. Stopping during a call leaves a FunctionCallContent
//   that no result ever matched, and providers reject a call with no result.
//
//   A history ending on a tool result. Stopping after a tool returned but before
//   the model answered leaves assistant(call) → tool(result), and the next send
//   puts a user message straight after the tool message. OpenAI accepts that;
//   Mistral rejects it ("Unexpected role 'user' after role 'tool'"). This is the
//   likelier stop of the two, since nothing is on screen while a tool runs.
//
// So the partial is cut after the last assistant message that carries text, and
// only then are unanswered calls dropped. Nothing the user saw is lost: only text
// streams to the screen, so the tool exchange after the last text was never
// visible.
//
// Pure, so these rules are unit tests rather than properties of a cancelled turn
// nobody can reproduce on demand.
internal static class PartialTurn
{
    // Null when the model never said anything — including a turn whose only
    // output was tool calls, answered or not.
    public static ChatResponse? Prune(ChatResponse response)
    {
        var lastAnswerIndex = FindLastAnswerIndex(response.Messages);
        if (lastAnswerIndex < 0)
        {
            return null;
        }

        var kept = response.Messages.Take(lastAnswerIndex + 1).ToList();

        // Counted over the kept range only: a result that fell after the cut no
        // longer answers anything that will be sent.
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

            // A message left with nothing, or with nothing but blank text, is the
            // shape an empty streaming chunk degrades to. Storing it would put a
            // silent assistant bubble in the transcript.
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

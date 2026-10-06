using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// The only writer of Microsoft.Extensions.AI's message types, so the only place
// that knows what a provider accepts. The system prompt travels separately, as
// ChatOptions.Instructions.
//
// The transcript keeps what the user saw; a provider will not always take it. So a
// tool call is sent only with a result and text after it, which drops the shapes
// that would break the chat for good once stored: a call with no result (every
// provider rejects it), and an answer ending on a tool result (Mistral rejects the
// next question: "Unexpected role 'user' after role 'tool'").
//
// A question that was never saved is not sent, so the model never works from a
// history a reload would not show. One saved but unanswered still is: "prøv igjen"
// needs it.
internal static class TranscriptRequest
{
    public static List<ChatMessage> Build(IReadOnlyList<Turn> turns)
    {
        var request = new List<ChatMessage>(turns.Count * 2);

        foreach (var turn in turns)
        {
            if (WasNeverSaved(turn))
            {
                continue;
            }

            request.Add(new ChatMessage(ChatRole.User, UserMessage(turn)));
            AppendAnswer(request, turn.Answer);
        }

        return request;
    }

    private static void AppendAnswer(List<ChatMessage> request, IReadOnlyList<TurnSegment> answer)
    {
        var sendable = answer.Take(FindLastTextIndex(answer) + 1);

        foreach (var round in sendable.GroupBy(segment => segment.Round))
        {
            var said = new List<AIContent>();
            var results = new List<AIContent>();

            foreach (var segment in round)
            {
                switch (segment)
                {
                    case TextSegment text when !string.IsNullOrWhiteSpace(text.Text):
                        said.Add(new TextContent(text.Text));
                        break;
                    case ToolSegment { Result: { } result } tool:
                        said.Add(new FunctionCallContent(tool.CallId, tool.ToolName, ToArguments(tool)));
                        results.Add(new FunctionResultContent(tool.CallId, result));
                        break;
                }
            }

            if (said.Count > 0)
            {
                request.Add(new ChatMessage(ChatRole.Assistant, said));
            }

            if (results.Count > 0)
            {
                request.Add(new ChatMessage(ChatRole.Tool, results));
            }
        }
    }

    private static string UserMessage(Turn turn)
    {
        var attachmentLine = AttachmentLine.Render(turn.Attachments);
        if (attachmentLine is null)
        {
            return turn.Prompt;
        }

        return $"{turn.Prompt}\n\n{attachmentLine}";
    }

    // A turn stopped, rather than failed, before its row was written carries no
    // stage and is still sent, as it was before turns existed.
    private static bool WasNeverSaved(Turn turn) =>
        turn.FailedAt is TurnStage.Authenticating or TurnStage.SavingMessage;

    private static int FindLastTextIndex(IReadOnlyList<TurnSegment> answer)
    {
        for (var index = answer.Count - 1; index >= 0; index--)
        {
            if (answer[index] is TextSegment text && !string.IsNullOrWhiteSpace(text.Text))
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<string, object?>? ToArguments(ToolSegment tool) =>
        tool.Arguments.ValueKind == JsonValueKind.Object
            ? tool.Arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value)
            : null;
}

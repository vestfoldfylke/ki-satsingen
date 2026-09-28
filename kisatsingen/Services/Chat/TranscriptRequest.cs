using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// Turns back into the messages a provider expects. The only place that writes
// Microsoft.Extensions.AI's message types, and so the only place that has to know
// what a provider will accept.
//
// The system prompt is not in the list: it travels as ChatOptions.Instructions
// and each adapter places it where its wire format wants it.
//
// A transcript stores what the user saw, which a provider will not always take.
// One rule keeps the two apart: a tool call is sent only if it has a result and
// the model said something after it. That drops the two shapes providers reject
// forever once stored:
//
//   an unanswered call — every provider rejects a call with no result;
//   an answer ending on a tool result — the next question would follow the tool
//   message directly, which Mistral rejects ("Unexpected role 'user' after role
//   'tool'").
//
// Nothing the model acted on is lost: a call counts only once text follows it.
internal static class TranscriptRequest
{
    public static List<ChatMessage> Build(IReadOnlyList<Turn> turns)
    {
        var request = new List<ChatMessage>(turns.Count * 2);

        foreach (var turn in turns)
        {
            request.Add(new ChatMessage(ChatRole.User, turn.Prompt));
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
        tool.Arguments.ValueKind == System.Text.Json.JsonValueKind.Object
            ? tool.Arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value)
            : null;
}

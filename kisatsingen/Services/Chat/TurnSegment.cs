using System.Text.Json;
using System.Text.Json.Serialization;

namespace kisatsingen.Services.Chat;

// One piece of an answer, in the order it happened. Ours rather than
// Microsoft.Extensions.AI's content types, so what is stored, rendered and sent
// back to the model is a shape this codebase owns and a package upgrade cannot
// change underneath stored rows.
//
// Round is the model round trip the segment came from: a tool result closes a
// round, and whatever the model says next opens the following one. Rebuilding
// the request groups by it rather than guessing where one round ended.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextSegment), "text")]
[JsonDerivedType(typeof(ToolSegment), "tool")]
public abstract record TurnSegment(Guid Id, int Round);

public sealed record TextSegment(Guid Id, int Round, string Text) : TurnSegment(Id, Round);

// CallId is the provider's, kept only to pair a call with its result and to send
// the pair back; Id is ours, and is what the UI keys on.
//
// Result is exactly what the model was given, a failure included, because the
// request has to replay it word for word.
public sealed record ToolSegment(
    Guid Id,
    int Round,
    string CallId,
    string ToolName,
    JsonElement Arguments,
    ToolStatus Status,
    JsonElement? Result) : TurnSegment(Id, Round);

[JsonConverter(typeof(JsonStringEnumConverter<ToolStatus>))]
public enum ToolStatus
{
    Running,
    Completed,

    // The tool threw. What failed goes to the log; the transcript only says that it did.
    Failed,

    // The turn ended while the tool was running, so there is no result.
    Interrupted
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace kisatsingen.Services.Chat;

// Ours rather than Microsoft.Extensions.AI's content types, so a package upgrade
// cannot change the shape of stored answers.
//
// Round lets the request be rebuilt per model round trip without guessing where
// one ended: a tool result closes a round.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextSegment), "text")]
[JsonDerivedType(typeof(ToolSegment), "tool")]
public abstract record TurnSegment(Guid Id, int Round);

public sealed record TextSegment(Guid Id, int Round, string Text) : TurnSegment(Id, Round);

// Id is ours and keys the UI; CallId is the provider's and only pairs the call
// with its result on the way back.
//
// Result is the text the provider was sent, not the value behind it: a value would
// be serialised again on replay, and a string result would arrive quoted.
public sealed record ToolSegment(
    Guid Id,
    int Round,
    string CallId,
    string ToolName,
    JsonElement Arguments,
    ToolStatus Status,
    string? Result) : TurnSegment(Id, Round);

[JsonConverter(typeof(ToolStatusJsonConverter))]
public enum ToolStatus
{
    Running,
    Completed,
    Failed,

    // The turn ended while the tool ran, so there is no result.
    Interrupted
}

// The default also reads integers, letting a stored 7 through as a status no code
// handles. Refused, it fails the read, which ChatTurnMapper already copes with.
internal sealed class ToolStatusJsonConverter() : JsonStringEnumConverter<ToolStatus>(namingPolicy: null, allowIntegerValues: false);

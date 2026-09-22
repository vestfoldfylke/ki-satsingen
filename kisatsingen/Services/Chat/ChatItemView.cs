using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public abstract record ChatItemView(Guid Id);

// ModelChangedTo names the model that answered this question, and is set only
// when that differs from the model that answered the previous one — the display
// name, so a transcript keeps reading correctly after a model is renamed or
// dropped from the catalogue.
//
// It hangs off the question rather than being its own item in the transcript
// because it has no existence apart from it: there is no such thing as a model
// boundary that does not head a question. Derived on every projection from the
// ModelKey each turn records, never stored.
public sealed record UserBubbleView(Guid Id, string Text, string? ModelChangedTo = null) : ChatItemView(Id);

// Something that happened in the chat but is not part of the conversation the
// model sees: a stop, a lost connection, a turn that broke. Detail carries the
// user-facing notice when there is one; ChatEventNotice falls back to a default
// per kind when there is not.
public sealed record ChatEventView(
    Guid Id,
    ChatEventKind Kind,
    string? Detail,
    DateTimeOffset At) : ChatItemView(Id);

public sealed record AssistantTurnView(
    Guid Id,
    IReadOnlyList<TurnPart> Parts,
    TurnMetadata? Metadata,
    DateTimeOffset? StartedAt) : ChatItemView(Id);

public sealed record TurnPart(string Text, IList<AIContent> Contents);

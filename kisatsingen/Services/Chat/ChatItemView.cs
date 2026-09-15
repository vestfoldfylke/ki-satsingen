using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public abstract record ChatItemView(Guid Id);

public sealed record UserBubbleView(Guid Id, string Text) : ChatItemView(Id);

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
    AssistantMetadata? Metadata,
    DateTimeOffset? StartedAt) : ChatItemView(Id);

public sealed record TurnPart(string Text, IList<AIContent> Contents);

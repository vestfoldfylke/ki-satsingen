using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public abstract record ChatItemView(Guid Id);

// ModelChangedTo is set only when this question was answered by a different
// model than the previous one. It sits on the question rather than being its own
// item because a model boundary always heads a question. Derived, never stored.
public sealed record UserBubbleView(Guid Id, string Text, string? ModelChangedTo = null) : ChatItemView(Id);

// Never sent to the model. Without a Detail, ChatEventNotice shows a default per kind.
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

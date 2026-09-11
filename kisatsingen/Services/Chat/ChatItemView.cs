using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public abstract record ChatItemView(Guid Id);

public sealed record UserBubbleView(Guid Id, string Text) : ChatItemView(Id);

// Something that happened in the chat but is not part of the conversation the
// model sees — currently only a user-initiated stop.
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

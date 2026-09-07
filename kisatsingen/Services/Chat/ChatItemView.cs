using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public abstract record ChatItemView(Guid Id);

public sealed record UserBubbleView(Guid Id, string Text) : ChatItemView(Id);

public sealed record AssistantTurnView(
    Guid Id,
    IReadOnlyList<TurnPart> Parts,
    AssistantMetadata? Metadata,
    DateTimeOffset? StartedAt) : ChatItemView(Id);

public sealed record TurnPart(string Text, IList<AIContent> Contents);

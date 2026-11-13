using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Entities;

public sealed class TokenUsage
{
    public const int MaxProviderLength = 64;
    public const int MaxModelIdLength = 128;
    public const int MaxStatusLength = 32;

    public Guid Id { get; init; }
    public required Guid UserId { get; init; }

    // No foreign keys: the row must outlive the chat, turn and assistant it names.
    public Guid? ChatId { get; init; }
    public Guid? TurnId { get; init; }
    public Guid? AssistantId { get; init; }

    public DateTimeOffset Timestamp { get; init; }
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? EstimatedInputTokens { get; init; }
    public long? EstimatedOutputTokens { get; init; }

    // Part of InputTokens, never added to it.
    public long? CachedInputTokens { get; init; }

    // Part of OutputTokens, never added to it.
    public long? ReasoningTokens { get; init; }

    public required TurnStatus Status { get; init; }
}

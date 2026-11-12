using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public sealed record TokenUsageEntry(
    Guid OwnerId,
    Guid ChatId,
    Guid TurnId,
    Guid? AssistantId,
    string Provider,
    string ModelId,
    MessageUsage Usage,
    TurnStatus Status);

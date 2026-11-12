namespace kisatsingen.Data.Entities;

public sealed record AssistantUsage(
    Guid AssistantId,
    string Name,
    int ChatCount,
    int UserCount,
    long InputTokens,
    long OutputTokens);

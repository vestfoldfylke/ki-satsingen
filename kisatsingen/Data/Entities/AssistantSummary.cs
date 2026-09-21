namespace kisatsingen.Data.Entities;

public sealed record AssistantSummary(Guid Id, string Name, string? Description, DateTimeOffset UpdatedAt);

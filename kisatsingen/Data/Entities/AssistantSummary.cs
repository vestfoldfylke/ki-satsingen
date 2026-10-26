namespace kisatsingen.Data.Entities;

public sealed record AssistantSummary(Guid Id, Guid OwnerId, string Name, string? Description, AssistantVisibility Visibility, DateTimeOffset UpdatedAt);

namespace kisatsingen.Data.Entities;

public sealed class Assistant
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 500;
    public const int MaxInstructionsLength = 8_000;
    public const int MaxKnowledgeFiles = 20;

    // All init, including what the updates change: they go through
    // ExecuteUpdateAsync and reads are AsNoTracking, so a setter would
    // advertise a mutation path that does not exist.
    public Guid Id { get; init; }
    public required Guid OwnerId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required AssistantVisibility Visibility { get; init; }

    // Not versioned: ChatTurn.SystemPrompt already records what each
    // turn was actually sent, so editing this cannot rewrite past chats.
    // Null until written: an assistant is created without them.
    public string? Instructions { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

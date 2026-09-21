namespace kisatsingen.Data.Entities;

public sealed class Assistant
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 500;

    // All init, including what UpdateAssistantAsync changes: that update goes
    // through ExecuteUpdateAsync and reads are AsNoTracking, so a setter would
    // advertise a mutation path that does not exist.
    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    // Not versioned: ChatMessage.SystemPromptSnapshot already records what each
    // turn was actually sent, so editing this cannot rewrite past chats.
    public required string Instructions { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public List<KnowledgeFile> KnowledgeFiles { get; init; } = [];
}

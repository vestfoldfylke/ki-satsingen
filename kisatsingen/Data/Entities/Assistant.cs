namespace kisatsingen.Data.Entities;

public sealed class Assistant
{
    // Shared by the column configuration and the repository's validation, so a
    // name that is too long is refused with an explanation instead of reaching
    // Postgres and coming back as a raw 22001.
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 500;

    // All init, including the fields UpdateAssistantAsync changes: that update
    // runs through ExecuteUpdateAsync, which never materialises an entity, and
    // reads are AsNoTracking. A setter here would advertise a mutation path that
    // does not exist. Chat does the same with Title and UpdatedAt.
    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    // The system prompt a chat started from this assistant is given. Deliberately
    // not versioned here: ChatMessage.SystemPromptSnapshot already records what
    // was actually sent on each turn, so editing an assistant changes what future
    // turns get without rewriting what past ones were told.
    public required string Instructions { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    // Deleting an assistant takes its files and their chunks with it. Chats
    // started from it survive with AssistantId set to null — see Chat.AssistantId.
    public List<KnowledgeFile> KnowledgeFiles { get; init; } = [];
}

namespace kisatsingen.Data.Entities;

public sealed class Chat
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? SystemPrompt { get; set; }

    // Nulled rather than cascaded when the assistant is deleted, so the transcript
    // survives; the assistant's knowledge files do not.
    public Guid? AssistantId { get; init; }

    // Snapshotted at creation and never overwritten, so the sidebar can still name
    // an assistant after it is deleted. Null iff the chat never had one.
    public string? AssistantNameSnapshot { get; init; }

    public List<ChatTurn> Turns { get; init; } = [];
}

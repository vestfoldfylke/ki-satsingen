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

    // Optional: a chat can be started from an assistant or from nothing. Nulled
    // rather than cascaded when the assistant is deleted, so the transcript
    // survives; the assistant's knowledge files do not.
    public Guid? AssistantId { get; init; }

    public List<ChatMessage> Messages { get; init; } = [];
    public List<ChatEvent> Events { get; init; } = [];
}

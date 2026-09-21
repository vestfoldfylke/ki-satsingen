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

    // Snapshotted from Assistant.Name at chat creation and never overwritten,
    // so the sidebar can still say "Chat with Legal Advisor (deleted)" after
    // the row is gone. Same principle as ChatMessage.SystemPromptSnapshot: past
    // chats keep their identifying context regardless of what happens to the
    // source. Null iff the chat was never bound to an assistant.
    public string? AssistantNameSnapshot { get; init; }

    public List<ChatMessage> Messages { get; init; } = [];
    public List<ChatEvent> Events { get; init; } = [];
}

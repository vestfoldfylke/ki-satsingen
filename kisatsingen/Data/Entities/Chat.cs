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

    // The assistant this chat was started from, if any. Chats without one stay
    // valid — this is an entry point, not a requirement.
    //
    // Nulled rather than cascaded when the assistant is deleted: the transcript
    // is still a true record of what was said, and each message already carries
    // its own SystemPromptSnapshot. What the chat does lose is the assistant's
    // knowledge files, which cascade away with their owner.
    //
    // No Assistant navigation to go with it, and no KnowledgeFiles collection:
    // no query traverses either, and a navigation nothing uses is an Include
    // waiting to pull a corpus into a list view. A chat's own files come from
    // IKnowledgeFileRepository.ListFilesForChatAsync, which projects.
    public Guid? AssistantId { get; init; }

    public List<ChatMessage> Messages { get; init; } = [];
    public List<ChatEvent> Events { get; init; } = [];
}

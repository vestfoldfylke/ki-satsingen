namespace kisatsingen.Data.Entities;

public sealed class Chat
{
    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? SystemPrompt { get; set; }

    public List<ChatMessage> Messages { get; init; } = [];
}

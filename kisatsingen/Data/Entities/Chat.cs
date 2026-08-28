namespace kisatsingen.Data.Entities;

public sealed class Chat
{
    public Guid Id { get; init; }
    public string? OwnerId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public List<ChatMessage> Messages { get; init; } = [];
}

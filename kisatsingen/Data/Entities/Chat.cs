namespace kisatsingen.Data.Entities;

public sealed class Chat
{
    public Guid Id { get; set; }
    public string? OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ChatMessage> Messages { get; set; } = [];
}

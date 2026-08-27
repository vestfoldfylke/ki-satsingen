namespace kisatsingen.Data.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public int SequenceNumber { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ContentsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public string? ResponseId { get; set; }
    public string? ModelId { get; set; }
    public string? FinishReason { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? TotalTokens { get; set; }
    public long? DurationMs { get; set; }
    public long? TimeToFirstTokenMs { get; set; }

    public Chat? Chat { get; set; }
}

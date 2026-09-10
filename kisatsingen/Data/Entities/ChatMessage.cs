namespace kisatsingen.Data.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }

    // Orders this message against events in the same chat. Both tables draw from
    // one sequence, so ordering is exact and independent of the clock.
    public long Seq { get; set; }

    public required string Role { get; init; }
    public required string Content { get; init; }
    public string? ContentsJson { get; init; }

    // Display only. Ordering is Seq's job.
    public DateTimeOffset CreatedAt { get; set; }

    public string? ResponseId { get; init; }
    public string? ModelId { get; init; }
    public string? FinishReason { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? DurationMs { get; init; }
    public long? TimeToFirstTokenMs { get; init; }

    public string? SystemPromptSnapshot { get; init; }

    public Chat? Chat { get; init; }
}

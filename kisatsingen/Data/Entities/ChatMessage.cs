namespace kisatsingen.Data.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }

    // Assigned by the database from a sequence shared with events, so ordering is
    // exact and independent of the clock.
    public long Seq { get; private set; }

    public required string Role { get; init; }

    // Content duplicates the text in ContentsJson on purpose: it is what a message
    // degrades to when the JSON can no longer be read. Don't remove it.
    public required string Content { get; init; }
    public string? ContentsJson { get; init; }

    // Which library version wrote ContentsJson: the difference between an
    // unreadable row and a migratable one.
    public string? ContentsSchemaVersion { get; init; }

    // Display only; Seq orders.
    public DateTimeOffset CreatedAt { get; set; }

    public string? ResponseId { get; init; }

    // What the provider says it served — a dated build, not what was asked for.
    public string? ModelId { get; init; }

    // Stored alongside ModelId because neither derives from the other. A key for a
    // model since removed is expected, so readers fall back rather than fail.
    public string? ModelKey { get; init; }

    public string? FinishReason { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? DurationMs { get; init; }
    public long? TimeToFirstTokenMs { get; init; }

    public string? SystemPromptSnapshot { get; init; }
}

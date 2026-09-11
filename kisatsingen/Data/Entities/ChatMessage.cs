namespace kisatsingen.Data.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }

    // Orders this message against events in the same chat. Assigned by the
    // database from a sequence both tables share, so ordering is exact, is
    // independent of the clock, and cannot be set — or mis-set — from here.
    public long Seq { get; private set; }

    public required string Role { get; init; }

    // Stored twice on purpose. Content is the plain text, ContentsJson the
    // structured parts the turn was made of. The text is derivable from the JSON,
    // and kept anyway: it is what a message degrades to when its contents can no
    // longer be deserialised, which is the difference between losing a tool call
    // and losing the conversation. Do not remove it as duplication.
    public required string Content { get; init; }
    public string? ContentsJson { get; init; }

    // Which Microsoft.Extensions.AI version wrote ContentsJson. AIContent is that
    // library's polymorphic hierarchy, not ours, so when a row stops
    // deserialising this is what says which version produced it — the difference
    // between an unreadable row and a migratable one.
    public string? ContentsSchemaVersion { get; init; }

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

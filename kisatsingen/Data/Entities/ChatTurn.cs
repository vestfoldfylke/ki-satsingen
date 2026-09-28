namespace kisatsingen.Data.Entities;

// One question and its answer. Inserted as Running when the question is sent and
// updated once when the turn ends, however it ends — so a row still Running
// after the fact is itself the record that the turn never finished.
//
// Status and FailedAt are the names of Services.Chat enums, kept as strings so
// a value this build does not know (after a rollback, say) degrades on read
// instead of making the chat unopenable.
public sealed class ChatTurn
{
    public const int MaxStatusLength = 32;
    public const int MaxModelKeyLength = 64;
    public const int MaxProviderIdLength = 128;
    public const int MaxFinishReasonLength = 64;

    public Guid Id { get; set; }
    public Guid ChatId { get; set; }

    // Assigned by the database, so ordering is exact and independent of the clock.
    public long Seq { get; private set; }

    public DateTimeOffset StartedAt { get; init; }

    public required string Prompt { get; init; }
    public required string SystemPrompt { get; init; }
    public required string ModelKey { get; init; }

    public required string Status { get; init; }
    public string? FailedAt { get; init; }

    // A JSON array of TurnSegment, in a schema this codebase owns.
    public required string AnswerJson { get; init; }

    // What the provider says it served — a dated build, not what was asked for.
    public string? ServedModelId { get; init; }
    public string? ResponseId { get; init; }
    public string? FinishReason { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? DurationMs { get; init; }
    public long? TimeToFirstTokenMs { get; init; }
}

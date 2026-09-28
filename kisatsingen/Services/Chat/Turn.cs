namespace kisatsingen.Services.Chat;

// One question, its answer and how it ended — the unit the page, the database
// and the request share, so none of them regroups messages.
//
// Immutable: a running turn is replaced by a newer snapshot, so a render never
// reads one the stream is changing.
public sealed record Turn
{
    public required Guid Id { get; init; }
    public required string Prompt { get; init; }

    // Snapshotted per turn, so the record matches what was sent even after the
    // chat's instructions change.
    public required string SystemPrompt { get; init; }

    // Null for a blank stored key: one bad field must not make a chat unopenable.
    public required ChatModelKey? ModelKey { get; init; }

    // Resolved at load, so a model since removed keeps a name.
    public required string? ModelDisplayName { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public IReadOnlyList<TurnSegment> Answer { get; init; } = [];

    // Answer is then empty rather than garbled, so the model is not sent it.
    public bool IsAnswerUnreadable { get; init; }

    public TurnStatus Status { get; init; } = TurnStatus.Running;
    public TurnStage? FailedAt { get; init; }
    public TurnMetadata? Metadata { get; init; }

    public static Turn Start(string prompt, ChatModel model, string systemPrompt) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = prompt,
        SystemPrompt = systemPrompt,
        ModelKey = model.Key,
        ModelDisplayName = model.DisplayName,
        StartedAt = DateTimeOffset.UtcNow
    };

    // A tool still running when its turn ends never will finish.
    public Turn EndedAs(TurnStatus status, TurnStage? failedAt = null) => this with
    {
        Status = status,
        FailedAt = status == TurnStatus.Failed ? failedAt : null,
        Answer = [.. Answer.Select(InterruptIfRunning)]
    };

    private static TurnSegment InterruptIfRunning(TurnSegment segment) =>
        segment is ToolSegment { Status: ToolStatus.Running } tool
            ? tool with { Status = ToolStatus.Interrupted }
            : segment;
}

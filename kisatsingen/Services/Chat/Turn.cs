namespace kisatsingen.Services.Chat;

// One exchange: the question, the answer and how it ended. The unit the
// transcript, the database and the UI all share, so none of them has to regroup
// messages to find out what belonged together.
//
// Immutable: a running turn is replaced by a newer snapshot, never changed in
// place, which is what lets a render read one without racing the stream.
public sealed record Turn
{
    public required Guid Id { get; init; }
    public required string Prompt { get; init; }

    // The instructions in force when the question was sent, and so the ones sent with it.
    public required string SystemPrompt { get; init; }

    // Null only for a stored key that is blank: one bad field must not make a chat unopenable.
    public required ChatModelKey? ModelKey { get; init; }

    // Resolved when the turn is built, so a model since removed keeps a name.
    public required string? ModelDisplayName { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public IReadOnlyList<TurnSegment> Answer { get; init; } = [];
    public TurnStatus Status { get; init; } = TurnStatus.Running;

    // Set only when Status is Failed.
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

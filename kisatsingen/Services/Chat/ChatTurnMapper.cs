using System.Text.Json;
using StoredTurn = kisatsingen.Data.Entities.ChatTurn;

namespace kisatsingen.Services.Chat;

internal static class ChatTurnMapper
{
    // How long after it started a Running row may still be being answered
    // somewhere else. A reload leaves the old circuit answering for up to Blazor's
    // retention period (3 minutes by default), and a long answer with tools can
    // outlast that; past this, nothing is still writing it.
    public static readonly TimeSpan StillRunningElsewhereFor = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web);

    public static StoredTurn ToEntity(Turn turn) => new()
    {
        Id = turn.Id,
        StartedAt = turn.StartedAt,
        Prompt = turn.Prompt,
        SystemPrompt = turn.SystemPrompt,
        ModelKey = turn.ModelKey?.Value ?? string.Empty,
        Status = turn.Status.ToString(),
        FailedAt = turn.FailedAt?.ToString(),
        AnswerJson = JsonSerializer.Serialize(turn.Answer, AnswerJson),
        ServedModelId = turn.Metadata?.ServedModelId,
        ResponseId = turn.Metadata?.ResponseId,
        FinishReason = turn.Metadata?.FinishReason,
        InputTokens = turn.Metadata?.Usage?.InputTokens,
        OutputTokens = turn.Metadata?.Usage?.OutputTokens,
        TotalTokens = turn.Metadata?.Usage?.TotalTokens,
        DurationMs = turn.Metadata?.DurationMs,
        TimeToFirstTokenMs = turn.Metadata?.TimeToFirstTokenMs
    };

    // resolveModelName must answer for keys no longer registered: a chat outlives
    // the models that answered it.
    //
    // loadedAt decides how a Running row reads, since only its age tells a turn
    // still being answered elsewhere from one whose ending was never written.
    public static Turn FromEntity(
        StoredTurn stored,
        DateTimeOffset loadedAt,
        Func<ChatModelKey, string> resolveModelName,
        ILogger logger)
    {
        var turn = Read(stored, resolveModelName, logger);

        return turn.Status == TurnStatus.Running && loadedAt - turn.StartedAt > StillRunningElsewhereFor
            ? turn.EndedAs(TurnStatus.Unfinished)
            : turn;
    }

    private static Turn Read(StoredTurn stored, Func<ChatModelKey, string> resolveModelName, ILogger logger)
    {
        var modelKey = ChatModelKey.TryCreate(stored.ModelKey);
        var answer = ReadAnswer(stored, logger);

        return new Turn
        {
            Id = stored.Id,
            Prompt = stored.Prompt,
            SystemPrompt = stored.SystemPrompt,
            ModelKey = modelKey,
            ModelDisplayName = modelKey is { } key ? resolveModelName(key) : null,
            StartedAt = stored.StartedAt,
            Answer = answer ?? [],
            IsAnswerUnreadable = answer is null,
            Status = ReadStatus(stored, logger),
            FailedAt = Enum.TryParse<TurnStage>(stored.FailedAt, out var stage) ? stage : null,
            Metadata = ReadMetadata(stored)
        };
    }

    // Null when the answer cannot be read. One unreadable answer must not lock the
    // user out of the whole chat. NotSupportedException is how System.Text.Json
    // reports a segment it cannot place, such as one without its "kind" first.
    private static IReadOnlyList<TurnSegment>? ReadAnswer(StoredTurn stored, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TurnSegment>>(stored.AnswerJson, AnswerJson) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(
                ex,
                "The stored answer for turn {TurnId} could not be read and is shown as unreadable. It was most likely written by a newer build with a segment kind this one does not know; redeploying that build restores it.",
                stored.Id);
            return null;
        }
    }

    private static TurnStatus ReadStatus(StoredTurn stored, ILogger logger)
    {
        if (Enum.TryParse<TurnStatus>(stored.Status, out var status))
        {
            return status;
        }

        logger.LogWarning(
            "Turn {TurnId} has status {Status}, which this build does not know. It is shown as unfinished.",
            stored.Id,
            stored.Status);
        return TurnStatus.Unfinished;
    }

    private static TurnMetadata? ReadMetadata(StoredTurn stored) => stored.DurationMs is null
        ? null
        : new TurnMetadata(
            stored.ServedModelId,
            stored.ResponseId,
            stored.FinishReason,
            MessageUsage.FromCounts(stored.InputTokens, stored.OutputTokens, stored.TotalTokens),
            stored.DurationMs,
            stored.TimeToFirstTokenMs);
}

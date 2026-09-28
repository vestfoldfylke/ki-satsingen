using System.Text.Json;
using StoredTurn = kisatsingen.Data.Entities.ChatTurn;

namespace kisatsingen.Services.Chat;

internal static class ChatTurnMapper
{
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
    public static Turn FromEntity(StoredTurn stored, Func<ChatModelKey, string> resolveModelName, ILogger logger)
    {
        var modelKey = ChatModelKey.TryCreate(stored.ModelKey);

        return new Turn
        {
            Id = stored.Id,
            Prompt = stored.Prompt,
            SystemPrompt = stored.SystemPrompt,
            ModelKey = modelKey,
            ModelDisplayName = modelKey is { } key ? resolveModelName(key) : null,
            StartedAt = stored.StartedAt,
            Answer = ReadAnswer(stored, logger),
            Status = ReadStatus(stored, logger),
            FailedAt = Enum.TryParse<TurnStage>(stored.FailedAt, out var stage) ? stage : null,
            Metadata = ReadMetadata(stored)
        };
    }

    // One unreadable answer must not lock the user out of the whole chat.
    private static IReadOnlyList<TurnSegment> ReadAnswer(StoredTurn stored, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TurnSegment>>(stored.AnswerJson, AnswerJson) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "The stored answer for turn {TurnId} could not be read and is shown as empty. It was most likely written by a newer build with a segment kind this one does not know; redeploying that build restores it.",
                stored.Id);
            return [];
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

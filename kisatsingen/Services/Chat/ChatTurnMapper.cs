using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoredTurn = kisatsingen.Data.Entities.ChatTurn;

namespace kisatsingen.Services.Chat;

internal static class ChatTurnMapper
{
    // Covers Blazor's circuit retention (3 minutes by default), during which a
    // reload leaves the old circuit answering, plus a long answer with tools.
    public static readonly TimeSpan StillRunningElsewhereFor = TimeSpan.FromMinutes(10);

    // Relaxed, or every æ, ø and å is stored as a six-character escape. Safe: this
    // JSON is only ever read back by this mapper, never rendered as HTML.
    private static readonly JsonSerializerOptions AnswerJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Nulls left out, so an attachment stores only the fields it has.
    private static readonly JsonSerializerOptions AttachmentsJson = new(AnswerJson)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
        AttachmentsJson = WriteAttachments(turn.Attachments),
        ServedModelId = turn.Metadata?.ServedModelId,
        ResponseId = turn.Metadata?.ResponseId,
        FinishReason = turn.Metadata?.FinishReason,
        InputTokens = turn.Metadata?.Usage?.InputTokens,
        OutputTokens = turn.Metadata?.Usage?.OutputTokens,
        EstimatedInputTokens = turn.Metadata?.Usage?.EstimatedInputTokens,
        EstimatedOutputTokens = turn.Metadata?.Usage?.EstimatedOutputTokens,
        DurationMs = turn.Metadata?.DurationMs,
        TimeToFirstTokenMs = turn.Metadata?.TimeToFirstTokenMs
    };

    // resolveModelName must answer for removed models too: a chat outlives them.
    // loadedAt is needed because only a Running row's age tells a turn still being
    // answered elsewhere from one whose ending was never written.
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
            Attachments = ReadAttachments(stored, logger),
            IsAnswerUnreadable = answer is null,
            Status = ReadStatus(stored, logger),
            FailedAt = Enum.TryParse<TurnStage>(stored.FailedAt, out var stage) ? stage : null,
            Metadata = ReadMetadata(stored)
        };
    }

    // Null rather than throwing: one unreadable answer must not lock the user out of
    // the chat. System.Text.Json throws NotSupportedException for a segment whose
    // "kind" is not first.
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

    // A guard: an ended turn has none still being processed (Turn.EndedAs
    // marks them interrupted), and one without a result must never be stored.
    private static string? WriteAttachments(IReadOnlyList<TurnAttachment> attachments)
    {
        var toStore = attachments.Where(attachment => !attachment.IsProcessing).ToList();
        if (toStore.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(toStore, AttachmentsJson);
    }

    // Empty rather than throwing, for the same reason as the answer. The prompt
    // is then replayed without its attachment line. A null entry is valid JSON
    // but would break every later request built from this chat, so it is dropped.
    private static IReadOnlyList<TurnAttachment> ReadAttachments(StoredTurn stored, ILogger logger)
    {
        if (stored.AttachmentsJson is null)
        {
            return [];
        }

        try
        {
            var attachments = JsonSerializer.Deserialize<List<TurnAttachment?>>(stored.AttachmentsJson, AttachmentsJson) ?? [];
            return [.. attachments.OfType<TurnAttachment>()];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "The stored attachments for turn {TurnId} could not be read, so its prompt is replayed without its attachment line.",
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
            MessageUsage.FromCounts(
                stored.InputTokens,
                stored.OutputTokens,
                stored.EstimatedInputTokens,
                stored.EstimatedOutputTokens),
            stored.DurationMs,
            stored.TimeToFirstTokenMs);
}

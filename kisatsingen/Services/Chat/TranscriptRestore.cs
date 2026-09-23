using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Services.Chat;

// Pure, so the merge is testable without a ChatSession.
internal static class TranscriptRestore
{
    // Messages and events share one Seq sequence, so a two-pointer merge restores
    // the written order with no ties — provided both lists arrive sorted by Seq,
    // as ChatRepository.GetChatAsync returns them.
    //
    // resolveModelName must answer for keys no longer registered: a chat outlives
    // the models that answered it.
    public static IReadOnlyList<TranscriptEntry> Build(
        IReadOnlyList<StoredMessage> messages,
        IReadOnlyList<ChatEvent> events,
        string systemPromptInForce,
        Func<ChatModelKey, string> resolveModelName,
        ILogger logger)
    {
        var entries = new List<TranscriptEntry>(messages.Count + events.Count);
        var messageIndex = 0;
        var eventIndex = 0;

        // So an answer reports the prompt in force when its question was sent.
        var currentSnapshot = systemPromptInForce;

        while (messageIndex < messages.Count || eventIndex < events.Count)
        {
            var takeMessage = eventIndex >= events.Count
                || (messageIndex < messages.Count && messages[messageIndex].Seq < events[eventIndex].Seq);

            if (takeMessage)
            {
                var stored = messages[messageIndex++];
                entries.Add(BuildMessageEntry(stored, ref currentSnapshot, resolveModelName, logger));
                continue;
            }

            var storedEvent = events[eventIndex++];
            entries.Add(new EventEntry(Guid.NewGuid(), storedEvent.Kind, storedEvent.Detail, storedEvent.CreatedAt));
        }

        return entries;
    }

    private static MessageEntry BuildMessageEntry(
        StoredMessage stored,
        ref string currentSnapshot,
        Func<ChatModelKey, string> resolveModelName,
        ILogger logger)
    {
        var role = new ChatRole(stored.Role);

        TurnMetadata? metadata = null;
        if (role == ChatRole.User)
        {
            currentSnapshot = stored.SystemPromptSnapshot ?? currentSnapshot;
        }
        else if (role == ChatRole.Assistant)
        {
            var modelKey = ChatModelKey.TryCreate(stored.ModelKey);

            metadata = new TurnMetadata(
                stored.ModelId,
                modelKey,
                modelKey is { } key ? resolveModelName(key) : null,
                stored.ResponseId,
                stored.FinishReason,
                MessageUsage.FromEntity(stored),
                stored.DurationMs,
                stored.TimeToFirstTokenMs,
                stored.CreatedAt,
                currentSnapshot);
        }

        return new MessageEntry(Guid.NewGuid(), ChatMessageMapper.FromEntity(stored, logger), metadata);
    }
}

using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Services.Chat;

// The inverse of TranscriptProjection: stored rows back into the one ordered
// sequence they were written as. Pure, so the merge can be tested without
// standing up a ChatSession and its six dependencies.
internal static class TranscriptRestore
{
    // Messages and events live in separate tables but share one sequence, so both
    // lists arrive sorted by Seq and no two rows can hold the same value. That is
    // what lets a straight two-pointer merge rebuild the original order with no
    // tie to break — and why callers must pass the lists already sorted by Seq,
    // which the ordered includes in ChatRepository.GetChatAsync do.
    // The logger is passed rather than resolved so this stays a function of its
    // arguments. It is only reached when a stored message's contents cannot be
    // read; see ChatMessageMapper.FromEntity.
    //
    // resolveModelName is passed for the same reason — naming a stored model key
    // needs the catalogue, and taking the catalogue itself would make this
    // untestable without one. It must answer for keys that are no longer
    // registered: a chat outlives the models that answered it.
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

        // Each user turn records the system prompt in force when it was sent, so
        // assistant metadata can report the prompt that actually produced it.
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
            var modelKey = stored.ModelKey is { } storedKey ? new ChatModelKey(storedKey) : (ChatModelKey?)null;

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

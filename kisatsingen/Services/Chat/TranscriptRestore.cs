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
    public static IReadOnlyList<TranscriptEntry> Build(
        IReadOnlyList<StoredMessage> messages,
        IReadOnlyList<ChatEvent> events,
        string systemPromptInForce)
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
                entries.Add(BuildMessageEntry(stored, ref currentSnapshot));
                continue;
            }

            var storedEvent = events[eventIndex++];
            entries.Add(new EventEntry(Guid.NewGuid(), storedEvent.Kind, storedEvent.Detail, storedEvent.CreatedAt));
        }

        return entries;
    }

    private static MessageEntry BuildMessageEntry(StoredMessage stored, ref string currentSnapshot)
    {
        var role = new ChatRole(stored.Role);

        AssistantMetadata? metadata = null;
        if (role == ChatRole.User)
        {
            currentSnapshot = stored.SystemPromptSnapshot ?? currentSnapshot;
        }
        else if (role == ChatRole.Assistant)
        {
            metadata = new AssistantMetadata(
                stored.ModelId,
                stored.ResponseId,
                stored.FinishReason,
                MessageUsage.FromEntity(stored),
                stored.DurationMs,
                stored.TimeToFirstTokenMs,
                stored.CreatedAt,
                currentSnapshot);
        }

        return new MessageEntry(Guid.NewGuid(), ChatMessageMapper.FromEntity(stored), metadata);
    }
}

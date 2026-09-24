using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// Owned by the caller and filled by the stream, so a stopped or failed turn still
// has everything it received once the exception has unwound.
internal sealed class TurnProgress
{
    private readonly List<ChatResponseUpdate> _updates = [];

    public long? FirstTokenMs { get; set; }

    // From the stopwatch, not the metric timer: the timer is only observed on
    // success, and a stopped turn still took time.
    public long DurationMs { get; set; }

    public bool HasUpdates => _updates.Count > 0;

    public void Add(ChatResponseUpdate update) => _updates.Add(update);

    // Also folds in every UsageContent that arrived, so a partial turn keeps the
    // measured usage of its completed round trips.
    public ChatResponse ToResponse() => _updates.ToChatResponse();
}

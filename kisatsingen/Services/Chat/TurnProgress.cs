using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// What a turn has produced so far.
//
// Exists because the streaming loop used to accumulate into a local, so a turn
// that was stopped or that failed lost everything it had already received the
// moment the exception unwound the frame — including the answer the user was
// reading, and including the usage that any completed tool round trip had
// already reported.
//
// Owned by the caller and filled by the streaming method, so every catch and the
// finally can still see it. That, rather than any cleverness in the persistence,
// is what makes a partial turn recoverable.
internal sealed class TurnProgress
{
    private readonly List<ChatResponseUpdate> _updates = [];

    public long? FirstTokenMs { get; set; }

    // Taken from the stopwatch rather than the metric timer: the timer is observed
    // only when the turn succeeds, and a stopped turn still lasted a length of
    // time worth recording against what it produced.
    public long DurationMs { get; set; }

    public bool HasUpdates => _updates.Count > 0;

    public void Add(ChatResponseUpdate update) => _updates.Add(update);

    // Folding the updates also folds in every UsageContent that arrived, so a
    // partial turn carries the real, provider-reported usage for whichever round
    // trips managed to finish. Only the interrupted one goes unreported.
    public ChatResponse ToResponse() => _updates.ToChatResponse();
}

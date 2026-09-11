using System.Text;

namespace kisatsingen.Services.Chat;

// Decides when a stream's accumulated text is due for the browser. Nagle-style:
// a solitary token in a slow stream goes out immediately, a burst is coalesced.
//
// Thresholds tuned for streams roughly in the 20-200 tok/s range. Flush more
// often -> more SignalR msgs/s and higher server CPU under fan-out; less often
// -> visible pauses in the streaming UI. Retune if either shows up under load.
//
// Pure and stateful-but-local, which is the point: the rule below is the most
// intricate logic in the chat path, and here it can be tested by feeding it
// timestamps with no Blazor circuit, no JS runtime and no model behind it.
internal sealed class FlushCadence
{
    private const long FlushIntervalMs = 50;
    private const int FlushCharThreshold = 400;

    private readonly StringBuilder _buffer = new();

    // Seeded a full interval in the past so the first token counts as
    // "long-idle" and goes out eagerly, giving a truthful TTFT on the client.
    private long _lastFlushMs = -FlushIntervalMs;

    // Returns the text now due for the browser, or null to keep buffering.
    //
    // offsetMs must never go backwards between calls. The window is measured
    // from the last flush, so a backward step leaves it unreachable and stalls
    // the visible stream until the buffer fills instead. Callers get this for
    // free from a Stopwatch; a wall clock does not qualify.
    //
    // Measuring from the last flush rather than the last token is what lets one
    // rule cover both cases: in a slow stream every token is already a full
    // interval past the previous flush and goes out alone, while a burst inside
    // one interval accumulates. An earlier version also tested the gap since the
    // last token. Given non-decreasing offsets that term is redundant — the last
    // flush is never more recent than the last token, so a token idle-far from
    // the previous token is idle-far from the previous flush too.
    public string? Append(long offsetMs, string text)
    {
        _buffer.Append(text);

        // Nothing buffered means nothing due, matching Drain. Without this an
        // empty token could send an empty chunk to the browser.
        if (_buffer.Length == 0)
        {
            return null;
        }

        var hasWindowElapsed = offsetMs - _lastFlushMs >= FlushIntervalMs;
        var isBufferFull = _buffer.Length >= FlushCharThreshold;

        if (!hasWindowElapsed && !isBufferFull)
        {
            return null;
        }

        _lastFlushMs = offsetMs;
        return Take();
    }

    // Whatever the last flush left behind, once the stream has ended.
    public string? Drain() => _buffer.Length > 0 ? Take() : null;

    private string Take()
    {
        var due = _buffer.ToString();
        _buffer.Clear();
        return due;
    }
}

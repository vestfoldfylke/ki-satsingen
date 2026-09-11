using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The flush rule, fed synthetic timestamps. No circuit, no JS runtime, no model
// — which is the reason it was worth pulling out of ChatSession at all.
public sealed class FlushCadenceTests
{
    private const long FlushIntervalMs = 50;
    private const int FlushCharThreshold = 400;

    // The seeding exists for this: a truthful time-to-first-token on the client
    // depends on the opening token not waiting for a flush window to elapse.
    [Fact]
    public void The_first_token_goes_out_immediately()
    {
        var cadence = new FlushCadence();

        var due = cadence.Append(0, "Hei");

        Assert.Equal("Hei", due);
    }

    [Fact]
    public void Tokens_arriving_faster_than_the_flush_window_are_coalesced()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var withinWindow = new[]
        {
            cadence.Append(10, "a"),
            cadence.Append(20, "b"),
            cadence.Append(30, "c")
        };

        Assert.All(withinWindow, Assert.Null);
    }

    [Fact]
    public void A_coalesced_burst_is_released_in_one_chunk_once_the_window_elapses()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");
        cadence.Append(10, "a");
        cadence.Append(20, "b");

        var due = cadence.Append(FlushIntervalMs + 1, "c");

        Assert.Equal("abc", due);
    }

    // A slow stream is the case the adaptive rule exists for: waiting out a
    // window would show the reader a stalled bubble between tokens.
    [Fact]
    public void A_token_after_an_idle_gap_goes_out_on_its_own()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var due = cadence.Append(FlushIntervalMs * 4, "alone");

        Assert.Equal("alone", due);
    }

    [Fact]
    public void A_burst_larger_than_the_buffer_threshold_is_released_before_the_window_elapses()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var due = cadence.Append(1, new string('x', FlushCharThreshold));

        Assert.Equal(new string('x', FlushCharThreshold), due);
    }

    [Fact]
    public void Draining_releases_what_the_last_flush_left_behind()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");
        cadence.Append(10, "tail");

        var remaining = cadence.Drain();

        Assert.Equal("tail", remaining);
    }

    [Fact]
    public void Draining_a_fully_flushed_stream_yields_nothing()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "everything");

        var remaining = cadence.Drain();

        Assert.Null(remaining);
    }

    // The window is inclusive, so a token landing exactly on the boundary is due
    // rather than held for another interval.
    [Fact]
    public void A_token_arriving_exactly_on_the_window_boundary_goes_out()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var due = cadence.Append(FlushIntervalMs, "boundary");

        Assert.Equal("boundary", due);
    }

    [Fact]
    public void A_buffer_one_character_short_of_the_threshold_keeps_waiting()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var due = cadence.Append(1, new string('x', FlushCharThreshold - 1));

        Assert.Null(due);
    }

    // Several tokens can share a millisecond on a fast stream, which makes the
    // window measurement zero. They must coalesce rather than each go out.
    [Fact]
    public void A_burst_arriving_in_the_same_millisecond_is_coalesced()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");

        var sameMillisecond = new[] { cadence.Append(0, "a"), cadence.Append(0, "b") };

        Assert.All(sameMillisecond, Assert.Null);
    }

    [Fact]
    public void An_empty_token_never_produces_an_empty_chunk()
    {
        var cadence = new FlushCadence();

        var due = cadence.Append(0, string.Empty);

        Assert.Null(due);
    }

    [Fact]
    public void Draining_twice_yields_nothing_the_second_time()
    {
        var cadence = new FlushCadence();
        cadence.Append(0, "first");
        cadence.Append(10, "tail");
        cadence.Drain();

        var second = cadence.Drain();

        Assert.Null(second);
    }

    // Pins the precondition rather than blessing the outcome. A backward offset
    // leaves the window unreachable, so the stream stalls until the buffer fills.
    // This is the failure a wall clock would cause and the reason ChatSession
    // times the stream with a Stopwatch. If anyone ever makes FlushCadence
    // tolerate this itself, this test is the tripwire that forces the decision.
    [Fact]
    public void An_offset_that_goes_backwards_stalls_the_window()
    {
        var cadence = new FlushCadence();
        cadence.Append(1000, "flushed a second in");

        var due = cadence.Append(100, "after a backward clock step");

        Assert.Null(due);
    }

    // Every token reaches the client exactly once: no chunk repeated by a flush
    // that forgot to clear, none dropped by one that cleared too eagerly.
    [Fact]
    public void Every_token_is_delivered_exactly_once_across_a_mixed_stream()
    {
        var cadence = new FlushCadence();
        var offsets = new long[] { 0, 5, 12, 60, 61, 62, 200, 201 };
        var delivered = new List<string>();

        for (var i = 0; i < offsets.Length; i++)
        {
            if (cadence.Append(offsets[i], i.ToString()) is { } due)
            {
                delivered.Add(due);
            }
        }

        if (cadence.Drain() is { } remaining)
        {
            delivered.Add(remaining);
        }

        Assert.Equal("01234567", string.Concat(delivered));
    }
}

using System.Diagnostics;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// Moves one model stream: records every update into the turn's progress, pushes
// its text to the browser at FlushCadence's pace, and counts tool activity.
//
// Knows nothing about the transcript, the database or the chat. ChatSession
// decides what a stream is for and what happens to it afterwards; this only
// carries it. That boundary is what lets ChatSession read as the sequence of a
// turn rather than as the mechanics of a stream.
internal sealed class TurnStreamer
{
    private readonly ChatClientChannel _channel;
    private readonly IMetricsService _metrics;
    private readonly string _metricPrefix;

    // The prefix is passed in rather than owned here so the series names stay
    // exactly what they were when this lived inside ChatSession. Dashboards are
    // keyed on those names.
    public TurnStreamer(ChatClientChannel channel, IMetricsService metrics, string metricPrefix)
    {
        _channel = channel;
        _metrics = metrics;
        _metricPrefix = metricPrefix;
    }

    // Writes into progress rather than returning, so a cancellation or provider
    // failure unwinding out of here leaves the caller holding everything that had
    // arrived. See TurnProgress.
    public async Task StreamAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> request,
        ChatOptions options,
        Guid streamId,
        TurnProgress progress,
        CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{_metricPrefix}_Duration", "Elapsed time for a chat message");

        // Stopwatch, not UtcNow: FlushCadence needs offsets that never go
        // backwards, and a wall clock does when NTP steps it — which would freeze
        // the visible stream until the clock caught up.
        var elapsed = Stopwatch.StartNew();
        var cadence = new FlushCadence();

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(request, options, ct))
            {
                progress.Add(update);
                var offsetMs = elapsed.ElapsedMilliseconds;

                CountToolActivity(update);

                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                progress.FirstTokenMs ??= offsetMs;

                if (cadence.Append(offsetMs, update.Text) is { } due)
                {
                    _ = _channel.StreamAppend(streamId, due);
                }
            }

            if (cadence.Drain() is { } remaining)
            {
                _ = _channel.StreamAppend(streamId, remaining);
            }
        }
        finally
        {
            // In a finally because a stopped turn still took time, and what it
            // produced is persisted against that figure. The histogram below stays
            // on the success path: changing which turns it observes would change
            // what the dashboards mean.
            progress.DurationMs = elapsed.ElapsedMilliseconds;
        }

        duration.ObserveDuration();
    }

    private void CountToolActivity(ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    _metrics.Count($"{_metricPrefix}_ToolCall", "Number of tool calls performed", ("Tool", call.Name));
                    break;
                case FunctionResultContent:
                    _metrics.Count($"{_metricPrefix}_ToolResult", "Number of tool results retrieved");
                    break;
            }
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// Carries a model stream to the browser. Knows nothing of the transcript or the
// database, so ChatSession reads as the sequence of a turn rather than the
// mechanics of a stream.
internal sealed class TurnStreamer
{
    private readonly ChatClientChannel _channel;
    private readonly IMetricsService _metrics;
    private readonly string _metricPrefix;

    // The prefix is passed in to keep the series names dashboards are keyed on.
    public TurnStreamer(ChatClientChannel channel, IMetricsService metrics, string metricPrefix)
    {
        _channel = channel;
        _metrics = metrics;
        _metricPrefix = metricPrefix;
    }

    // Writes into progress rather than returning, so an exception unwinding out of
    // here leaves the caller holding everything that arrived.
    public async Task StreamAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> request,
        ChatOptions options,
        Guid streamId,
        TurnProgress progress,
        CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{_metricPrefix}_Duration", "Elapsed time for a chat message");

        // Not UtcNow: FlushCadence needs offsets that never go backwards, and an NTP
        // step would freeze the visible stream.
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
            // A stopped turn is persisted with its duration too.
            progress.DurationMs = elapsed.ElapsedMilliseconds;
        }

        // Success only: observing other turns would change what the dashboards mean.
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

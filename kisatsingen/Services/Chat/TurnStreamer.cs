using System.Diagnostics;
using kisatsingen.Constants;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// Split by how often things fire: text goes per token to the browser's stream for
// its segment, with no Blazor render; a segment or tool starting or finishing
// re-renders the page, which is rare enough to afford.
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

    // Writes into the builder rather than returning, so a caller whose stream threw
    // still has everything that arrived.
    public async Task StreamAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> request,
        ChatOptions options,
        TurnBuilder builder,
        Action onStructureChanged,
        CancellationToken ct)
    {
        var duration = _metrics.Histogram($"{_metricPrefix}_Duration", "Elapsed time for a chat message");

        // Not UtcNow: FlushCadence needs offsets that never go backwards, and an NTP
        // step would freeze the visible stream.
        var elapsed = Stopwatch.StartNew();

        // Renewed per segment, so one segment's leftovers never land in the next.
        var cadence = new FlushCadence();

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(request, options, ct))
            {
                CountContent(update);
                var offsetMs = elapsed.ElapsedMilliseconds;

                foreach (var change in builder.Apply(update))
                {
                    switch (change)
                    {
                        case TurnChange.TextStarted started:
                            cadence = new FlushCadence();
                            // The render only queues, so the stream may start before the element
                            // exists; chat-streaming.ts looks it up per frame, which makes that safe.
                            _ = _channel.StreamStart(started.SegmentId);
                            onStructureChanged();
                            break;

                        case TurnChange.TextAppended appended:
                            builder.TimeToFirstTokenMs ??= offsetMs;
                            if (cadence.Append(offsetMs, appended.Delta) is { } due)
                            {
                                _ = _channel.StreamAppend(appended.SegmentId, due);
                            }
                            break;

                        // Before the render, or the stream's last paint could land over the
                        // page's final render of the segment.
                        case TurnChange.TextEnded ended:
                            EndStream(ended.SegmentId, cadence);
                            break;

                        case TurnChange.ToolStarted or TurnChange.ToolFinished:
                            onStructureChanged();
                            break;
                    }
                }
            }

            if (builder.OpenTextSegmentId is { } openId && cadence.Drain() is { } remaining)
            {
                _ = _channel.StreamAppend(openId, remaining);
            }
        }
        finally
        {
            // Even when stopped: the browser holds a buffer per stream.
            if (builder.OpenTextSegmentId is { } openId)
            {
                _ = _channel.StreamEnd(openId);
            }

            // In the finally, so a stopped turn is stored with its duration too.
            builder.DurationMs = elapsed.ElapsedMilliseconds;
        }

        // Success only: observing other turns would change what the dashboards mean.
        duration.ObserveDuration();
    }

    private void EndStream(Guid segmentId, FlushCadence cadence)
    {
        if (cadence.Drain() is { } remaining)
        {
            _ = _channel.StreamAppend(segmentId, remaining);
        }

        _ = _channel.StreamEnd(segmentId);
    }

    private void CountContent(ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    _metrics.Count($"{_metricPrefix}_ToolCall", "Number of tool calls performed", (MetricConstants.MetricsToolLabelName, call.Name));
                    break;
                case FunctionResultContent:
                    _metrics.Count($"{_metricPrefix}_ToolResult", "Number of tool results retrieved");
                    break;
                case TextContent or UsageContent:
                    break;
                // TurnBuilder drops these; counted so a kind an upgrade starts sending is noticed.
                default:
                    _metrics.Count($"{_metricPrefix}_UnkeptContent", "Streamed content the transcript does not keep", (MetricConstants.MetricsContentTypeLabelName, content.GetType().Name));
                    break;
            }
        }
    }
}

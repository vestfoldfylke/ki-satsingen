using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// The only reader of Microsoft.Extensions.AI's streamed content. Filled as the
// stream runs rather than returned at the end, so a stopped or failed turn keeps
// everything that arrived.
//
// Content it does not keep (reasoning, citations, hosted tools) is dropped;
// TurnStreamer counts it, so a new kind shows up in metrics.
internal sealed class TurnBuilder
{
    private static readonly JsonSerializerOptions ContentJson = AIJsonUtilities.DefaultOptions;

    private readonly Turn _started;
    private readonly List<TurnSegment> _segments = [];
    private readonly Dictionary<string, int> _toolIndexByCallId = new(StringComparer.Ordinal);

    // Rather than replacing the segment's record on every token.
    private readonly StringBuilder _openText = new();
    private int _openTextIndex = -1;

    private int _round;
    private bool _isRoundClosed;

    private string? _servedModelId;
    private string? _responseId;
    private string? _finishReason;
    private UsageDetails? _usage;

    public TurnBuilder(Turn started) => _started = started;

    // Set by the stream, which owns the clock.
    public long? TimeToFirstTokenMs { get; set; }
    public long? DurationMs { get; set; }

    public Guid? OpenTextSegmentId => _openTextIndex < 0 ? null : _segments[_openTextIndex].Id;

    public IReadOnlyList<TurnChange> Apply(ChatResponseUpdate update)
    {
        RecordMetadata(update);

        var changes = new List<TurnChange>(1);
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    AppendText(text.Text, changes);
                    break;
                case FunctionCallContent call:
                    StartTool(call, changes);
                    break;
                case FunctionResultContent result:
                    FinishTool(result, changes);
                    break;
                case UsageContent usage:
                    AddUsage(usage.Details);
                    break;
            }
        }

        return changes;
    }

    public Turn Snapshot() => _started with { Answer = SnapshotAnswer() };

    public Turn Finish(TurnStatus status, TurnStage? failedAt = null) =>
        (Snapshot() with { Metadata = BuildMetadata() }).EndedAs(status, failedAt);

    private void AppendText(string text, List<TurnChange> changes)
    {
        OpenNextRoundIfClosed();

        if (_openTextIndex < 0)
        {
            var opened = new TextSegment(Guid.NewGuid(), _round, string.Empty);
            _segments.Add(opened);
            _openTextIndex = _segments.Count - 1;
            changes.Add(new TurnChange.TextStarted(opened.Id));
        }

        _openText.Append(text);
        changes.Add(new TurnChange.TextAppended(_segments[_openTextIndex].Id, text));
    }

    private void StartTool(FunctionCallContent call, List<TurnChange> changes)
    {
        OpenNextRoundIfClosed();
        CloseText(changes);

        var tool = new ToolSegment(
            Guid.NewGuid(),
            _round,
            call.CallId,
            call.Name,
            JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>(), ContentJson),
            ToolStatus.Running,
            Result: null);

        _toolIndexByCallId[call.CallId] = _segments.Count;
        _segments.Add(tool);
        changes.Add(new TurnChange.ToolStarted(tool.Id));
    }

    private void FinishTool(FunctionResultContent result, List<TurnChange> changes)
    {
        // Text the model wrote after its call belongs to this round; left open, the
        // next round's text would be appended to it and replayed before the result.
        CloseText(changes);
        _isRoundClosed = true;

        // A result for a call this stream never carried has nothing to attach to.
        if (!_toolIndexByCallId.TryGetValue(result.CallId, out var index) || _segments[index] is not ToolSegment tool)
        {
            return;
        }

        _segments[index] = tool with
        {
            Status = result.Exception is null ? ToolStatus.Completed : ToolStatus.Failed,
            Result = AsSentToProvider(result.Result)
        };
        changes.Add(new TurnChange.ToolFinished(tool.Id));
    }

    // The OpenAI adapter's rule, which every model here goes through, so the stored
    // text replays byte for byte (pinned by ToolResultReplayTests). Another
    // adapter would need its own rule.
    private static string? AsSentToProvider(object? result) => result switch
    {
        null => null,
        string text => text,
        _ => JsonSerializer.Serialize(result, ContentJson)
    };

    private void CloseText(List<TurnChange> changes)
    {
        if (_openTextIndex < 0)
        {
            return;
        }

        var closed = (TextSegment)_segments[_openTextIndex] with { Text = _openText.ToString() };
        _segments[_openTextIndex] = closed;
        _openText.Clear();
        _openTextIndex = -1;
        changes.Add(new TurnChange.TextEnded(closed.Id));
    }

    private void OpenNextRoundIfClosed()
    {
        if (!_isRoundClosed)
        {
            return;
        }

        _round++;
        _isRoundClosed = false;
    }

    private IReadOnlyList<TurnSegment> SnapshotAnswer()
    {
        var answer = _segments.ToArray();
        if (_openTextIndex >= 0)
        {
            answer[_openTextIndex] = (TextSegment)answer[_openTextIndex] with { Text = _openText.ToString() };
        }

        return answer;
    }

    private void RecordMetadata(ChatResponseUpdate update)
    {
        _servedModelId = update.ModelId ?? _servedModelId;
        _responseId = update.ResponseId ?? _responseId;
        _finishReason = update.FinishReason?.Value ?? _finishReason;
    }

    // Summed across round trips: what the turn cost, not how big the context is.
    private void AddUsage(UsageDetails details)
    {
        _usage ??= new UsageDetails();
        _usage.Add(details);
    }

    private TurnMetadata? BuildMetadata() => DurationMs is null
        ? null
        : new TurnMetadata(
            _servedModelId,
            _responseId,
            _finishReason,
            MessageUsage.FromCounts(_usage?.InputTokenCount, _usage?.OutputTokenCount, _usage?.TotalTokenCount),
            DurationMs,
            TimeToFirstTokenMs);
}

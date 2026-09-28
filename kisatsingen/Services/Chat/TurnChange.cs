namespace kisatsingen.Services.Chat;

// What one streamed update did to the answer, in order. Text changes are for the
// browser's stream; the rest change what the page shows and need a render.
internal abstract record TurnChange
{
    public sealed record TextStarted(Guid SegmentId) : TurnChange;

    public sealed record TextAppended(Guid SegmentId, string Delta) : TurnChange;

    // The model moved on to a tool, so this segment will grow no further.
    public sealed record TextEnded(Guid SegmentId) : TurnChange;

    public sealed record ToolStarted(Guid SegmentId) : TurnChange;

    public sealed record ToolFinished(Guid SegmentId) : TurnChange;
}

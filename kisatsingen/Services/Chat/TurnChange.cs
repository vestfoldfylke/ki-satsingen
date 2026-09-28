namespace kisatsingen.Services.Chat;

// Text changes go to the browser's stream; the rest need a page render.
internal abstract record TurnChange
{
    public sealed record TextStarted(Guid SegmentId) : TurnChange;

    public sealed record TextAppended(Guid SegmentId, string Delta) : TurnChange;

    public sealed record TextEnded(Guid SegmentId) : TurnChange;

    public sealed record ToolStarted(Guid SegmentId) : TurnChange;

    public sealed record ToolFinished(Guid SegmentId) : TurnChange;
}

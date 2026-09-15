namespace kisatsingen.Services.Chat;

// The cancellation state of one turn, and the only thing that knows why a turn
// was cancelled.
//
// Two sources, linked into the one token the turn actually awaits. Splitting
// them is what lets a caller tell a user stop apart from a lost circuit after
// the fact: whichever source was cancelled is the cause, read from
// CancellationTokenSource state that is safe to observe cross-thread by design.
// No side-channel field, no ordering requirement, and a future third path
// (timeout, admin abort) adds a third source rather than a new value on a shared
// enum.
//
// Deliberately knows nothing about transcripts, metrics or event kinds. It
// answers "was this cancelled, and by which side" and leaves what to call that
// to the caller that has the vocabulary for it.
internal sealed class TurnCancellation : IDisposable
{
    private readonly CancellationTokenSource _user = new();
    private readonly CancellationTokenSource _disconnect = new();
    private readonly CancellationTokenSource _linked;

    public TurnCancellation() =>
        _linked = CancellationTokenSource.CreateLinkedTokenSource(_user.Token, _disconnect.Token);

    // The token the turn awaits. Fires when either source is cancelled.
    public CancellationToken Token => _linked.Token;

    // Whether this turn was cancelled by us at all. The distinction matters
    // because an OperationCanceledException can arrive from somewhere that never
    // asked either of these sources — a provider's own HTTP timeout, say — and a
    // caller that assumes otherwise files a real failure as an intended stop.
    public bool IsCancelled => _user.IsCancellationRequested || _disconnect.IsCancellationRequested;

    // Disconnect wins if both fired on the same turn: pressing stop on a dying
    // tab is functionally a disconnect, and the connectivity signal is the more
    // useful one to keep.
    public bool IsDisconnect => _disconnect.IsCancellationRequested;

    // The user asked for the turn to end.
    public void CancelForUser() => TryCancel(_user);

    // The browser stopped listening — the transport dropped, the tab closed, the
    // circuit was torn down.
    public void CancelForDisconnect() => TryCancel(_disconnect);

    // The async form, for teardown paths. Cancel() runs the linked source's
    // callbacks on the calling thread, which can resume the cancelled turn's
    // continuation inline — so a dispose could find itself waiting on the turn's
    // own error handling, database write included. CancelAsync hands those
    // callbacks to the thread pool instead.
    public async Task CancelForDisconnectAsync()
    {
        try
        {
            await _disconnect.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Same reasoning as TryCancel: already disposed is already cancelled.
        }
    }

    // Cancelling either underlying source is what fires the linked token;
    // cancelling the linked source directly does not propagate back to them, and
    // would leave IsCancelled reading false for a turn that was very much
    // cancelled.
    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Raced Dispose. A disposed source is already cancelled from a
            // caller's perspective, so swallowing is correct — and both callers
            // are notifications ("the user pressed stop", "the circuit died")
            // that have nothing left to do once the turn is gone.
        }
    }

    // Every observation above stays readable after this runs: IsCancellationRequested
    // is safe on a disposed source, which is what lets a caller still ask why a
    // turn ended while the turn's own teardown races it.
    //
    // Linked first, then the sources it observed — reversing risks the linked one
    // dereferencing an already-disposed underlying token.
    public void Dispose()
    {
        _linked.Dispose();
        _user.Dispose();
        _disconnect.Dispose();
    }
}

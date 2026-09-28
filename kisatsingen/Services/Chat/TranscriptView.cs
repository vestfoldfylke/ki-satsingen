namespace kisatsingen.Services.Chat;

// ModelChangedTo is set only when this turn ran on a different model than the
// one before it: a model boundary always heads a question.
public sealed record TurnView(Turn Turn, bool IsLive, string? ModelChangedTo);

// What the page renders, derived from the turns. Pure, so the derivations are
// tested without a ChatSession.
internal static class TranscriptView
{
    // How long a Running turn this session is not streaming may still be running
    // somewhere else. A reload leaves the old circuit answering for up to Blazor's
    // retention period (3 minutes by default), and a long answer with tools can
    // outlast that; past this, nothing is still writing it.
    public static readonly TimeSpan StillRunningElsewhereFor = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<TurnView> Build(IReadOnlyList<Turn> turns, Guid? liveTurnId, DateTimeOffset now)
    {
        var views = new List<TurnView>(turns.Count);
        ChatModelKey? previousModel = null;

        foreach (var turn in turns)
        {
            var isLive = turn.Id == liveTurnId;

            // A null on either side means a stored key that could not be read, not a change.
            var modelChangedTo = previousModel is { } earlier && turn.ModelKey is { } current && earlier != current
                ? turn.ModelDisplayName ?? current.Value
                : null;
            previousModel = turn.ModelKey ?? previousModel;

            views.Add(new TurnView(AsSeenNow(turn, isLive, now), isLive, modelChangedTo));
        }

        return views;
    }

    // A Running turn this session is not streaming either is still being answered
    // elsewhere — another tab, or the circuit a reload left behind — or its ending
    // was never written and it will not finish now. Only age tells them apart.
    private static Turn AsSeenNow(Turn turn, bool isLive, DateTimeOffset now) =>
        turn.Status == TurnStatus.Running && !isLive && now - turn.StartedAt > StillRunningElsewhereFor
            ? turn.EndedAs(TurnStatus.Unfinished)
            : turn;
}

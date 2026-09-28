namespace kisatsingen.Services.Chat;

// ModelChangedTo is set only when this turn ran on a different model than the
// one before it: a model boundary always heads a question.
public sealed record TurnView(Turn Turn, bool IsLive, string? ModelChangedTo);

// What the page renders, derived from the turns. Pure, so the derivations are
// tested without a ChatSession.
internal static class TranscriptView
{
    public static IReadOnlyList<TurnView> Build(IReadOnlyList<Turn> turns, Guid? liveTurnId)
    {
        var views = new List<TurnView>(turns.Count);
        ChatModelKey? previousModel = null;

        foreach (var turn in turns)
        {
            // A null on either side means a stored key that could not be read, not a change.
            var modelChangedTo = previousModel is { } earlier && turn.ModelKey is { } current && earlier != current
                ? turn.ModelDisplayName ?? current.Value
                : null;
            previousModel = turn.ModelKey ?? previousModel;

            views.Add(new TurnView(turn, turn.Id == liveTurnId, modelChangedTo));
        }

        return views;
    }
}

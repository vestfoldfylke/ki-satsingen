namespace kisatsingen.Services.Chat;

public sealed record TurnView(Turn Turn, bool IsLive, string? ModelChangedTo);

// Pure, so it is tested without a ChatSession.
internal static class TranscriptView
{
    public static IReadOnlyList<TurnView> Build(IReadOnlyList<Turn> turns, Guid? liveTurnId)
    {
        var views = new List<TurnView>(turns.Count);
        ChatModelKey? previousModel = null;

        foreach (var turn in turns)
        {
            // A null is a stored key that could not be read, not a change of model.
            var modelChangedTo = previousModel is { } earlier && turn.ModelKey is { } current && earlier != current
                ? turn.ModelDisplayName ?? current.Value
                : null;
            previousModel = turn.ModelKey ?? previousModel;

            views.Add(new TurnView(turn, turn.Id == liveTurnId, modelChangedTo));
        }

        return views;
    }
}

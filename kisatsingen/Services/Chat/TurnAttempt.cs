using System.Diagnostics;

namespace kisatsingen.Services.Chat;

// No status, stage or outcome of its own: all follow from the turn, so the attempt
// cannot disagree with what is shown, stored and counted.
internal sealed record TurnAttempt(Turn Turn, Exception? Failure = null)
{
    public TurnOutcome Outcome => Turn.Status switch
    {
        TurnStatus.Completed => TurnOutcome.Success,
        TurnStatus.Stopped => TurnOutcome.Stopped,
        TurnStatus.LeftChat => TurnOutcome.LeftChat,
        TurnStatus.Disconnected => TurnOutcome.Disconnected,
        TurnStatus.Failed => TurnOutcome.Failed,
        _ => throw new UnreachableException($"A turn ended as {Turn.Status}, which no attempt ends as; map it to an outcome here.")
    };

    public string? ServedModelId => Outcome == TurnOutcome.Success ? Turn.Metadata?.ServedModelId : null;

    // Completed is saved already, and a failed answer save is not retried: the row
    // would then hold the whole answer under a notice saying it was not stored.
    public bool ShouldSaveTurn =>
        Turn.Status != TurnStatus.Completed && Turn.FailedAt != TurnStage.SavingResponse;
}

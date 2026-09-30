namespace kisatsingen.Services.Chat;

// No status or stage of its own: the turn carries them, so the attempt cannot
// disagree with what is shown and stored.
internal sealed record TurnAttempt(Turn Turn, TurnOutcome Outcome, Exception? Failure = null)
{
    public string? ServedModelId => Outcome == TurnOutcome.Success ? Turn.Metadata?.ServedModelId : null;

    // Completed is saved already, and a failed answer save is not retried: the row
    // would then hold the whole answer under a notice saying it was not stored.
    public bool ShouldSaveTurn =>
        Turn.Status != TurnStatus.Completed && Turn.FailedAt != TurnStage.SavingResponse;
}

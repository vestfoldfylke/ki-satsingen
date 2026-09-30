namespace kisatsingen.Services.Chat;

// How a send attempt went, decided once before anything acts on it. The turn itself
// carries the status and failed stage, so nothing here can disagree with what is
// shown and stored.
internal sealed record TurnAttempt(Turn Turn, TurnOutcome Outcome, Exception? Failure = null)
{
    // Success is labelled with the model the provider served; the rest with the one requested.
    public string? ServedModelId => Outcome == TurnOutcome.Success ? Turn.Metadata?.ServedModelId : null;

    // A completed turn is stored already. One whose answer failed to save is not
    // retried: the row would hold the whole answer under a notice saying it was not stored.
    public bool ShouldSaveTurn =>
        Turn.Status != TurnStatus.Completed && Turn.FailedAt != TurnStage.SavingResponse;
}

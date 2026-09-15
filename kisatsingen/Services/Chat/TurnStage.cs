namespace kisatsingen.Services.Chat;

// How far a turn got before it ended. Only meaningful when a turn did not
// finish, and then it is the single most useful thing known about the failure:
// it tells the user which of their work was lost and ops which dependency broke.
//
// Deliberately a position in the turn rather than a classification of the
// exception. The exception itself goes to the log whole, where there is room for
// it; what survives into a metric label and a persisted, user-facing notice has
// to be a small bounded set that is safe to render, and this is that set.
internal enum TurnStage
{
    Authenticating,
    SavingMessage,
    Generating,
    SavingResponse
}

internal static class TurnStageNotice
{
    // What the user lost, in their terms. Derived from the stage rather than the
    // exception: a stage is a bounded set written for a reader of the transcript,
    // an exception message is unbounded text written for a reader of the logs and
    // routinely carries connection strings and provider error bodies — which this
    // is both persisted into and rendered from.
    public static string Describe(TurnStage stage) => stage switch
    {
        TurnStage.Authenticating => "Innlogging kunne ikke bekreftes",
        TurnStage.SavingMessage => "Meldingen ble ikke lagret",
        TurnStage.Generating => "Svaret kunne ikke fullføres",
        // The one case the user cannot see for themselves: the answer is on their
        // screen, complete, and will not be there after a reload.
        TurnStage.SavingResponse => "Svaret ble vist, men ikke lagret",
        _ => "Noe gikk galt under generering"
    };
}

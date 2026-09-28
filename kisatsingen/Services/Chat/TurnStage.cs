namespace kisatsingen.Services.Chat;

// A position in the turn rather than a classification of the exception: it lands
// in metric labels and stored rows, so it must be a small bounded set that is
// safe to render. The exception itself goes to the log.
//
// Persisted by name: reorder freely, never rename.
public enum TurnStage
{
    Authenticating,
    SavingMessage,
    Generating,
    SavingResponse
}

internal static class TurnStageNotice
{
    // From the stage, never the exception: exception messages carry connection
    // strings and provider error bodies.
    public static string Describe(TurnStage stage) => stage switch
    {
        TurnStage.Authenticating => "Innlogging kunne ikke bekreftes",
        TurnStage.SavingMessage => "Meldingen ble ikke lagret",
        TurnStage.Generating => "Svaret kunne ikke fullføres",
        TurnStage.SavingResponse => "Svaret ble vist, men ikke lagret",
        _ => "Noe gikk galt under generering"
    };
}

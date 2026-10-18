namespace kisatsingen.Services.Chat;

// A position in the turn rather than a classification of the exception: it lands
// in metric labels and stored rows, so it must be a small bounded set that is
// safe to render. The exception itself goes to the log.
//
// Stored by name, so renaming a member needs a migration that renames the stored values.
public enum TurnStage
{
    Authenticating,
    SavingMessage,
    ProcessingAttachments,
    Generating,
    SavingResponse
}

internal static class TurnStageMetric
{
    // Mapped explicitly, like TurnOutcomeMetric: the stored name is PascalCase,
    // and a metric label value is snake_case.
    public static string LabelValue(TurnStage stage) => stage switch
    {
        TurnStage.Authenticating => "authenticating",
        TurnStage.SavingMessage => "saving_message",
        TurnStage.ProcessingAttachments => "processing_attachments",
        TurnStage.Generating => "generating",
        TurnStage.SavingResponse => "saving_response",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Every stage needs a metric label; add one to TurnStageMetric.")
    };
}

internal static class TurnStageNotice
{
    // From the stage, never the exception: exception messages carry connection
    // strings and provider error bodies.
    public static string Describe(TurnStage stage) => stage switch
    {
        TurnStage.Authenticating => "Innlogging kunne ikke bekreftes",
        TurnStage.SavingMessage => "Meldingen ble ikke lagret",
        TurnStage.ProcessingAttachments => "Vedleggene kunne ikke behandles",
        TurnStage.Generating => "Svaret kunne ikke fullføres",
        TurnStage.SavingResponse => "Svaret ble vist, men ikke lagret",
        _ => "Noe gikk galt under generering"
    };
}

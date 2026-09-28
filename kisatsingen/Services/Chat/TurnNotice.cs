namespace kisatsingen.Services.Chat;

// The line under a turn that did not simply answer. Null when there is nothing
// to say: the turn is streaming here, or it answered.
internal static class TurnNotice
{
    public static string? Describe(TurnView view) => view.Turn switch
    {
        { IsAnswerUnreadable: true } => "Svaret kunne ikke leses",
        { Status: TurnStatus.Running } when view.IsLive => null,
        // Being answered by another tab, or by the circuit a reload left behind.
        { Status: TurnStatus.Running } => "Svaret skrives fortsatt. Last inn siden på nytt om litt for å se det.",
        { Status: TurnStatus.Completed } => null,
        { Status: TurnStatus.Stopped } => "Generering stoppet",
        { Status: TurnStatus.LeftChat } => "Generering stoppet fordi du forlot samtalen",
        { Status: TurnStatus.Disconnected } => "Tilkoblingen ble brutt under generering",
        { Status: TurnStatus.Unfinished } => "Svaret ble ikke fullført",
        { Status: TurnStatus.Failed, FailedAt: { } stage } => TurnStageNotice.Describe(stage),
        _ => "Noe gikk galt under generering"
    };
}

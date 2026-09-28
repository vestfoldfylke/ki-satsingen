namespace kisatsingen.Services.Chat;

internal static class TurnNotice
{
    public static string? Describe(TurnView view) => view.Turn switch
    {
        { IsAnswerUnreadable: true } => "Svaret kunne ikke leses",
        { Status: TurnStatus.Running } when view.IsLive => null,
        // Still being answered elsewhere, or never saved: nothing here can tell which.
        { Status: TurnStatus.Running } => "Svaret er ikke ferdig, eller ble ikke lagret. Last inn siden på nytt om litt.",
        { Status: TurnStatus.Completed } => null,
        { Status: TurnStatus.Stopped } => "Generering stoppet",
        { Status: TurnStatus.LeftChat } => "Generering stoppet fordi du forlot samtalen",
        // A closed tab and a lost connection look the same on the server.
        { Status: TurnStatus.Disconnected } => "Generering stoppet fordi siden ble lukket eller mistet forbindelsen",
        { Status: TurnStatus.Unfinished } => "Svaret ble ikke fullført",
        { Status: TurnStatus.Failed, FailedAt: { } stage } => TurnStageNotice.Describe(stage),
        _ => "Noe gikk galt under generering"
    };
}

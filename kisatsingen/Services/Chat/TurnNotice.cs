namespace kisatsingen.Services.Chat;

// The line under a turn that did not simply answer. Null when there is nothing
// to say: the turn is still running, or it answered.
internal static class TurnNotice
{
    public static string? Describe(Turn turn) => turn.Status switch
    {
        TurnStatus.Running or TurnStatus.Completed => null,
        TurnStatus.Stopped => "Generering stoppet",
        TurnStatus.LeftChat => "Generering stoppet fordi du forlot samtalen",
        TurnStatus.Disconnected => "Tilkoblingen ble brutt under generering",
        TurnStatus.Unfinished => "Svaret ble ikke fullført",
        TurnStatus.Failed when turn.FailedAt is { } stage => TurnStageNotice.Describe(stage),
        _ => "Noe gikk galt under generering"
    };
}

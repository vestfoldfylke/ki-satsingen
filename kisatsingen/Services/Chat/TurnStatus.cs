namespace kisatsingen.Services.Chat;

// Persisted by name: reorder freely, never rename.
public enum TurnStatus
{
    Running,
    Completed,
    Stopped,

    // Apart from Stopped so the transcript never claims the user pressed stop.
    LeftChat,
    Disconnected,

    Failed,

    // Never written. ChatTurnMapper derives it for a Running row too old for
    // anything to still be answering it.
    Unfinished
}

internal static class TurnStatusExtensions
{
    // Beside the enum, so a status for a new cancellation cause is added here too.
    public static bool IsCancellation(this TurnStatus status) =>
        status is TurnStatus.Stopped or TurnStatus.LeftChat or TurnStatus.Disconnected;
}

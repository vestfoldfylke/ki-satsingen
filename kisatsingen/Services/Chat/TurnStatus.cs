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

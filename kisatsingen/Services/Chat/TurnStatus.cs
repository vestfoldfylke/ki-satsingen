namespace kisatsingen.Services.Chat;

// How a turn stands. Persisted by name, so members can be reordered but not renamed.
public enum TurnStatus
{
    // Written when the turn starts, and replaced when it ends.
    Running,
    Completed,
    Stopped,

    // The user opened another chat or deleted this one mid-turn. Apart from
    // Stopped because they never pressed stop.
    LeftChat,

    // The circuit was discarded mid-turn. Apart from Stopped for the same reason.
    Disconnected,

    Failed,

    // Never written. A turn read back as Running long after it started, so nothing
    // is still running it: the process died mid-turn, or its final save failed.
    // ChatTurnMapper derives it when a chat is loaded.
    Unfinished
}

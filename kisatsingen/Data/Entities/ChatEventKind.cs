namespace kisatsingen.Data.Entities;

// Things that happened in a chat but are not part of the conversation the model
// sees. Persisted as strings so the column stays readable and stays stable if
// the enum is ever reordered.
public enum ChatEventKind
{
    // The user pressed stop while a response was being generated. The partial
    // response is deliberately not kept — stop means stop.
    Stopped,

    // The turn ended in an error the user did not cause: the provider broke
    // mid-stream, a save did not go through. Any partial output is discarded the
    // same way a stop discards it, and for the same reason — what is kept is the
    // notice, so a reload explains the gap instead of presenting a broken turn as
    // an empty one.
    Failed,

    // The Blazor circuit was discarded mid-turn, e.g. once the reconnect period
    // after a closed tab or network drop ran out before the answer did. Not a
    // user-initiated stop — kept apart from Stopped so the reload transcript
    // does not tell the user they pressed a button they did not.
    Disconnected,

    // The user opened another chat or deleted this one mid-turn, which stops it.
    // Apart from Stopped for the same reason as Disconnected: they never pressed stop.
    LeftChat
}

namespace kisatsingen.Data.Entities;

// Things that happened in a chat but are not part of the conversation the model
// sees. Persisted as strings so the column stays readable and stays stable if
// the enum is ever reordered.
public enum ChatEventKind
{
    // The user pressed stop while a response was being generated. The partial
    // response is deliberately not kept — stop means stop.
    Stopped,

    // The turn ended in an error the user did not cause. Not written yet: see
    // the recoverable-vs-fatal classification still to be decided.
    Failed
}

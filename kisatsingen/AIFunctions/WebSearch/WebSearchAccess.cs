namespace kisatsingen.AIFunctions.WebSearch;

// Whether a turn is offered search_web, and whether the tool may search.
internal enum WebSearchAccess
{
    NotOffered,

    // The user's switch is off. Still offered, so the prompt cache holds when
    // the switch changes, and the tool's refusal can tell the model to let the
    // user know that search can be turned on.
    SwitchedOff,

    Allowed,
}

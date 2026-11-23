namespace kisatsingen.Data.Entities;

// What the usage was spent on. A row is one model's own calls for one piece
// of work, like a line on an invoice: a turn that searched has a ChatTurn row
// and a WebSearch row with the same TurnId, so a turn's spend is the sum of
// its rows. Persisted by name.
public enum UsageKind
{
    // The chat model's rounds in one turn, including the tool results it reads.
    ChatTurn,

    KnowledgeFileConversion,

    // All web searches in one turn. WebSearchCount is always set.
    //
    // The row has tokens too, not just the count: the search runs through a
    // chat client, so the search model's tokens are billed as well. If we
    // move to a pure web search API, the tokens will be null.
    WebSearch
}

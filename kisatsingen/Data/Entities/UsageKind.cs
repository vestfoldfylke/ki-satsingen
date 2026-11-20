namespace kisatsingen.Data.Entities;

// What the usage was spent on. Persisted by name.
public enum UsageKind
{
    // Measured in tokens.
    ChatTurn,

    // Measured in tokens.
    KnowledgeFileConversion
}

namespace kisatsingen.Services.KnowledgeFiles.Documents;

// Lines are 1-based and inclusive, as the read tools number them. Level is the
// heading level, and 0 for tables and blocks, which have none.
public sealed record OutlineEntry(int Line, int EndLine, OutlineKind Kind, int Level, string Title);

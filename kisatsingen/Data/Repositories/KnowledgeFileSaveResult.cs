using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

// Refusals the user can act on. A draft that breaks the repository's own
// contract (a malformed hash, a scope that is not the caller's) is a bug in
// the caller and throws instead.
public abstract record KnowledgeFileSaveResult
{
    public sealed record Saved(KnowledgeFileMetadata File) : KnowledgeFileSaveResult;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : KnowledgeFileSaveResult;
}

using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Converted and ready to save, not yet saved. Carries nothing derived from the
// Markdown (line count, outline, token estimate): the save path computes those
// itself, so they can never disagree with the text.
public sealed record KnowledgeFileDraft(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Markdown,
    ContentOrigin Origin,
    int? PageCount,
    string? Summary);

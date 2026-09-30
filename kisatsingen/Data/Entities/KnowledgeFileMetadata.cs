using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Data.Entities;

// Everything about a file except its text. A projection rather than the
// entity, so no file list, panel or duplicate check can load megabytes of
// Markdown by accident.
//
// ContentOrigin is null when the stored name is one this build does not know,
// such as a route added by a newer build; the tools treat that as the least
// trusted origin.
public sealed record KnowledgeFileMetadata(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string? Summary,
    int LineCount,
    int EstimatedTokenCount,
    ContentOrigin? ContentOrigin,
    int? PageCount,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

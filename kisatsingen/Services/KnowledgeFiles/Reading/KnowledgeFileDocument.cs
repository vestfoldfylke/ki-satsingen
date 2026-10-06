using kisatsingen.Data.Entities;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Reading;

// A file opened for reading: its lines and outline, both computed from the
// stored Markdown, so every tool numbers lines the same way.
public sealed record KnowledgeFileDocument(KnowledgeFileMetadata File, string[] Lines, IReadOnlyList<OutlineEntry> Outline);

namespace kisatsingen.AIFunctions.FileTools;

// What the model receives, serialised in camelCase with null fields left out.
// Model behaviour depends on these shapes, so they are pinned by tests.
internal static class FileToolResults
{
    public sealed record ListFilesResult(IReadOnlyList<ListedFile> Files);

    public sealed record ListedFile(string FileId, string Source, string FileName, int LineCount, int EstimatedTokens, string? Summary, string? OriginNote);

    public sealed record GetOutlineResult(
        string FileId,
        string FileName,
        int LineCount,
        int EstimatedTokens,
        string? OriginNote,
        IReadOnlyList<OutlineItem> Entries,
        string? Continuation);

    public sealed record OutlineItem(int StartLine, int EndLine, string Kind, int? Level, string Title);

    // Content last: it is the long part, and everything the model needs to
    // judge it comes before.
    public sealed record ReadFileResult(
        string FileId,
        string FileName,
        int StartLine,
        int EndLine,
        int LineCount,
        string? OriginNote,
        string? Continuation,
        string ContentNotice,
        string Content);

    // What read_file returns instead of content when the whole file does not fit
    // in one read, so the model chooses which part to read.
    public sealed record ReadFileTooLargeResult(
        string FileId,
        string FileName,
        int LineCount,
        int EstimatedTokens,
        string Continuation);
}

namespace kisatsingen.Services.KnowledgeFiles.Documents;

// The one definition of what a line is, shared by the outline, the save path's
// line count and the read tools, so their line numbers always agree. Expects
// Markdown with '\n' line endings: the save path normalises every stored text
// to that, so anything read back from the database already has them.
public static class DocumentLines
{
    // A final newline ends the last line rather than starting an empty one,
    // the way editors count.
    public static string[] Split(string markdown)
    {
        var lines = markdown.Split('\n');
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // Split(markdown).Length, without allocating every line.
    public static int Count(string markdown)
    {
        var newlineCount = markdown.AsSpan().Count('\n');
        return markdown.EndsWith('\n') ? newlineCount : newlineCount + 1;
    }
}

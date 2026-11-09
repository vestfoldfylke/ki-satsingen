namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes code as Markdown: a code span for inline code, a fenced block for
// code paragraphs. The code is written as it is, with no escapes; the fence
// is one backtick longer than the longest run of backticks in the code, so
// the code can't end it early.
internal static class DocxMarkdownCode
{
    private const char Backtick = '`';
    private const int MinBlockFenceLength = 3;

    // A span starting or ending with a backtick needs a space inside the
    // fence, which CommonMark strips again.
    public static string Span(string code)
    {
        var fence = new string(Backtick, LongestBacktickRun(code) + 1);
        var padding = code.StartsWith(Backtick) || code.EndsWith(Backtick)
            ? " "
            : "";

        return $"{fence}{padding}{code}{padding}{fence}";
    }

    // Blank lines at the start and the end are left out; those inside are code.
    public static string Block(IReadOnlyList<string> lines)
    {
        var code = string.Join("\n", lines).Trim('\n');
        var fence = new string(Backtick, Math.Max(LongestBacktickRun(code) + 1, MinBlockFenceLength));

        return $"{fence}\n{code}\n{fence}";
    }

    private static int LongestBacktickRun(string code)
    {
        var longest = 0;
        var current = 0;

        foreach (var character in code)
        {
            current = character == Backtick
                ? current + 1
                : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}

using kisatsingen.Services;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.AIFunctions.FileTools;

// Content is the lines numbered like `cat -n`, the form models already know
// from reading code. ContinueFromLine is set when the cap cut the range short.
// RangeEndLine is end clamped to the file and extended to a table's end.
internal sealed record LineWindowResult(int FirstLine, int LastLine, string Content, int? ContinueFromLine, int RangeEndLine);

// Which lines one read_file call returns. Pure: the tool validates the range
// before calling, so start is a line of the file and end is not before it.
internal static class LineWindow
{
    public static LineWindowResult Read(string[] lines, IReadOnlyList<OutlineEntry> outline, int start, int end, int maxTokens, int maxLineCharacters)
    {
        var rangeEnd = ExtendToTableEnd(outline, Math.Min(end, lines.Length));
        var numberedLines = new List<string>();
        var characterCount = 0;
        var lastRead = start - 1;

        for (var lineNumber = start; lineNumber <= rangeEnd; lineNumber++)
        {
            var numbered = $"{lineNumber}\t{TruncateLongLine(lines[lineNumber - 1], maxLineCharacters)}";

            // The first line is always returned, so every call makes progress;
            // the line cap keeps it far below the token cap.
            var countWithLine = characterCount + numbered.Length + 1;
            if (numberedLines.Count > 0 && TokenEstimate.FromCharacters(countWithLine) > maxTokens)
            {
                break;
            }

            numberedLines.Add(numbered);
            characterCount = countWithLine;
            lastRead = lineNumber;
        }

        int? continueFromLine = null;
        if (lastRead < rangeEnd)
        {
            continueFromLine = lastRead + 1;
        }

        return new LineWindowResult(start, lastRead, string.Join('\n', numberedLines), continueFromLine, rangeEnd);
    }

    // A read that stops mid-table leaves rows without the header that names
    // their columns, so it runs on to the table's end; the token cap still
    // applies, and a table too big for it is cut like any other text.
    private static int ExtendToTableEnd(IReadOnlyList<OutlineEntry> outline, int end)
    {
        var tableAroundEnd = outline.FirstOrDefault(entry => entry.Kind == OutlineKind.Table && entry.Line <= end && end < entry.EndLine);
        return tableAroundEnd?.EndLine ?? end;
    }

    private static string TruncateLongLine(string line, int maxLineCharacters)
    {
        if (line.Length <= maxLineCharacters)
        {
            return line;
        }

        var kept = TextTruncation.Prefix(line, maxLineCharacters);
        return kept + FileToolTexts.LineCutMarker(kept.Length, line.Length);
    }
}

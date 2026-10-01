namespace kisatsingen.AIFunctions.FileTools;

// The factory states minimum 1 in the schema but does not enforce it, and it
// binds a null line number as 0, so both are caught here.
internal static class LineRange
{
    // Null when the range is usable. An end past the last line is not an
    // error; both tools clamp it. A start past the last line is checked before
    // the order of the two, because get_outline defaults end to the last line,
    // and the line count is the answer that helps.
    public static string? FindError(int start, int end, int lineCount)
    {
        if (start < 1 || end < 1)
        {
            return FileToolTexts.InvalidLineNumber;
        }

        if (start > lineCount)
        {
            return FileToolTexts.StartPastLastLine(lineCount);
        }

        if (end < start)
        {
            return FileToolTexts.EndBeforeStart;
        }

        return null;
    }
}

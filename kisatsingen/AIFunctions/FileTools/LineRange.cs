namespace kisatsingen.AIFunctions.FileTools;

// The factory states minimum 1 in the schema but does not enforce it, and it
// binds a null line number as 0, so both are caught here.
internal static class LineRange
{
    // Null when the range is usable. An end past the last line is not an
    // error; both tools clamp it. A start past the last line is checked before
    // the order of the two, because get_outline defaults endLine to the last
    // line, and the line count is the answer that helps.
    public static string? FindError(int startLine, int endLine, int lineCount)
    {
        if (startLine < 1 || endLine < 1)
        {
            return FileToolTexts.InvalidLineNumber;
        }

        if (startLine > lineCount)
        {
            return FileToolTexts.StartPastLastLine(lineCount);
        }

        if (endLine < startLine)
        {
            return FileToolTexts.EndBeforeStart;
        }

        return null;
    }
}

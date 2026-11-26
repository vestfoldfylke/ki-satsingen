namespace kisatsingen.AIFunctions.KnowledgeFileTools;

// The factory states minimum 1 in the schema but does not enforce it, so a
// line number below 1 is caught here.
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
            return KnowledgeFileToolTexts.InvalidLineNumber;
        }

        if (startLine > lineCount)
        {
            return KnowledgeFileToolTexts.StartPastLastLine(lineCount);
        }

        if (endLine < startLine)
        {
            return KnowledgeFileToolTexts.EndBeforeStart;
        }

        return null;
    }
}

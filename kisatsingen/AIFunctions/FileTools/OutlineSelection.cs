using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.AIFunctions.FileTools;

// ContinueFromLine is set when the entry cap cut the selection short.
internal sealed record OutlineSelectionResult(IReadOnlyList<OutlineEntry> Entries, int? ContinueFromLine);

// Which outline entries one get_outline call returns. Pure.
internal static class OutlineSelection
{
    // Depth counts the heading levels the file uses, so a file whose top
    // headings are ## still shows two levels at depth 2. Counted from the
    // whole file rather than the range, so a continuation shows the same
    // levels as the page before it. Tables and blocks are always shown.
    public static OutlineSelectionResult Select(IReadOnlyList<OutlineEntry> outline, int start, int end, int depth, int maxEntries)
    {
        var inRange = outline.Where(entry => entry.Line >= start && entry.Line <= end).ToList();

        var shownLevels = outline
            .Where(entry => entry.Kind == OutlineKind.Heading)
            .Select(entry => entry.Level)
            .Distinct()
            .Order()
            .Take(depth)
            .ToHashSet();

        var selected = inRange
            .Where(entry => entry.Kind != OutlineKind.Heading || shownLevels.Contains(entry.Level))
            .ToList();

        if (selected.Count <= maxEntries)
        {
            return new OutlineSelectionResult(selected, ContinueFromLine: null);
        }

        var shown = selected.Take(maxEntries).ToList();
        var firstHidden = selected[maxEntries];
        return new OutlineSelectionResult(shown, ContinueFromLine: firstHidden.Line);
    }
}

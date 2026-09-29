using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace kisatsingen.Services.KnowledgeFiles.Documents;

// Headings and tables with the lines they cover, computed from the Markdown on
// every call and never stored, so it cannot drift from the text. Parsed with
// Markdig rather than matched with regex: a '#' inside a code block is not a
// heading, and underlined (setext) headings count.
public static class DocumentOutline
{
    // Roughly a screen of text to skim per entry, for documents with no
    // headings to go by.
    private const int FallbackBlockLines = 100;

    // A block may run past its target to end at a blank line, but not forever.
    private const int MaxFallbackBlockLines = 200;

    private const int MaxTitleLength = 120;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static IReadOnlyList<OutlineEntry> Build(string markdown)
    {
        var lines = DocumentLines.Split(markdown);
        var lineStarts = FindLineStarts(markdown);
        var document = Markdown.Parse(markdown, Pipeline);

        var headings = BuildHeadings(document, lineStarts, lines.Length);
        var tables = document.Descendants<Table>()
            .Select(table =>
            {
                var firstLine = LineOf(lineStarts, table.Span.Start);
                return new OutlineEntry(
                    firstLine,
                    LineOf(lineStarts, table.Span.End),
                    OutlineKind.Table,
                    Level: 0,
                    Truncate("Tabell: " + lines[firstLine - 1].Trim().Trim('|').Trim()));
            });

        var structure = headings.Count > 0
            ? [.. BuildPreamble(lines, headings[0].Line), .. headings]
            : BuildFallbackBlocks(lines);

        return structure.Concat(tables)
            .OrderBy(entry => entry.Line)
            .ThenBy(entry => entry.Kind)
            .ToList();
    }

    // A heading covers everything until the next heading at its own level or
    // above, so a section includes its subsections. Start lines come from the
    // source span, not Block.Line, which for an underlined heading is the
    // underline rather than the text.
    private static List<OutlineEntry> BuildHeadings(MarkdownDocument document, List<int> lineStarts, int lineCount)
    {
        var headings = document.Descendants<HeadingBlock>().ToList();
        var startLines = headings.Select(heading => LineOf(lineStarts, heading.Span.Start)).ToList();
        var entries = new List<OutlineEntry>(headings.Count);

        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            var nextSection = Enumerable.Range(index + 1, headings.Count - index - 1)
                .FirstOrDefault(next => headings[next].Level <= heading.Level, -1);
            var endLine = nextSection < 0 ? lineCount : startLines[nextSection] - 1;

            entries.Add(new OutlineEntry(startLines[index], endLine, OutlineKind.Heading, heading.Level, Truncate(TextOf(heading))));
        }

        return entries;
    }

    // Text before the first heading belongs to no section, so without its own
    // entry a model navigating by outline would never see an introduction.
    private static IEnumerable<OutlineEntry> BuildPreamble(string[] lines, int firstHeadingLine)
    {
        var lastPreambleLine = firstHeadingLine - 1;
        if (FirstTextLine(lines, 1, lastPreambleLine) is { } title)
        {
            yield return new OutlineEntry(1, lastPreambleLine, OutlineKind.Block, Level: 0, Truncate(title));
        }
    }

    // Blocks of about FallbackBlockLines, each ended at a blank line when one
    // comes soon enough, so a block rarely cuts a paragraph.
    private static List<OutlineEntry> BuildFallbackBlocks(string[] lines)
    {
        var entries = new List<OutlineEntry>();
        var start = 0;

        while (start < lines.Length)
        {
            var end = Math.Min(start + FallbackBlockLines, lines.Length) - 1;
            while (end < lines.Length - 1 && !string.IsNullOrWhiteSpace(lines[end]) && end - start + 1 < MaxFallbackBlockLines)
            {
                end++;
            }

            if (FirstTextLine(lines, start + 1, end + 1) is { } title)
            {
                entries.Add(new OutlineEntry(start + 1, end + 1, OutlineKind.Block, Level: 0, Truncate(title)));
            }

            start = end + 1;
        }

        return entries;
    }

    // Between two 1-based lines, inclusive. Null when they hold only blank
    // lines, which are not worth an outline entry.
    private static string? FirstTextLine(string[] lines, int firstLine, int lastLine) =>
        lines[(firstLine - 1)..lastLine].FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();

    // From the parsed inlines, so emphasis and code in a heading read as text
    // and the markers of either heading style never appear in the title.
    private static string TextOf(HeadingBlock heading)
    {
        var text = new StringBuilder();
        foreach (var inline in heading.Inline?.Descendants<Inline>() ?? [])
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
            }
        }

        return text.ToString().Trim();
    }

    private static List<int> FindLineStarts(string markdown)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < markdown.Length; index++)
        {
            if (markdown[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts;
    }

    // The 1-based line holding a character offset.
    private static int LineOf(List<int> lineStarts, int offset)
    {
        var index = lineStarts.BinarySearch(offset);
        return (index >= 0 ? index : ~index - 1) + 1;
    }

    private static string Truncate(string title) =>
        title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
}

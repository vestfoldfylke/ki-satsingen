using DocumentFormat.OpenXml.Wordprocessing;
using static kisatsingen.Services.KnowledgeFiles.Conversion.Office.OpenXmlValues;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

internal readonly record struct DocxListItem(DocxListReference List, DocxListKind Kind);

// Writes list items, and the paragraphs that continue one: an indented
// paragraph without a marker, right after an item, is part of that item in
// Word, so it is indented under the item's text.
internal sealed class DocxListWriter(DocxContent content, DocxBlockOutput blocks)
{
    private const int IndentPerListLevel = 4;
    private const string BulletMarker = "- ";
    private const string HardLineBreak = "  \n";

    // The number each list's levels are at. Word counts a list across the
    // whole document, also past other paragraphs between its items.
    private readonly Dictionary<int, Dictionary<int, int>> _numbersByLevelByListId = [];
    private int _previousLevel;

    // Where the last item's text starts, which continuing paragraphs line up with.
    private string _textIndent = "";

    // The paragraph's own list wins over its style's. A paragraph that sets
    // only its level is at that level of its style's list.
    public DocxListItem? ItemOf(ParagraphProperties? properties, string? styleId)
    {
        var ownList = properties?.NumberingProperties;
        var ownListId = ValueOf(ownList?.NumberingId?.Val);
        var ownLevel = ValueOf(ownList?.NumberingLevelReference?.Val);
        var styleList = content.Styles.ListOf(styleId);

        var listId = ownListId ?? styleList?.ListId;
        if (listId is null)
        {
            return null;
        }

        var list = new DocxListReference(listId.Value, ownLevel ?? (ownListId is null ? styleList?.Level : null) ?? 0);
        return content.Lists.KindOf(list) is { } kind
            ? new DocxListItem(list, kind)
            : null;
    }

    // The paragraph's own indent wins over its style's, also when it is 0.
    public bool ContinuesItem(ParagraphProperties? properties, string? styleId) =>
        blocks.IsInList && (DocxStyles.ReadLeftIndent(properties?.Indentation) ?? content.Styles.LeftIndentOf(styleId)) > 0;

    // A list may skip levels in Word, but four spaces too many make a code
    // block in Markdown, so an item is at most one level below the one before.
    // A numbered item gets its number as Word shows it, since the model reads
    // the Markdown as text, and a reference such as "punkt 5" needs it.
    public void WriteItem(DocxListItem item, List<string> lines)
    {
        var number = NextNumber(item.List);

        var maxLevel = blocks.IsInList
            ? _previousLevel + 1
            : 0;
        var level = Math.Clamp(item.List.Level, 0, maxLevel);
        _previousLevel = level;

        var indent = new string(' ', level * IndentPerListLevel);
        var marker = item.Kind == DocxListKind.Bulleted
            ? BulletMarker
            : $"{number}. ";
        _textIndent = new string(' ', indent.Length + marker.Length);

        blocks.ListItem($"{indent}{marker}{IndentedText(lines)}");
    }

    public void WriteContinuation(List<string> lines) => blocks.ListContinuation(_textIndent + IndentedText(lines));

    private string IndentedText(List<string> lines) =>
        string.Join(HardLineBreak + _textIndent, lines.Select(DocxMarkdownEscape.BlockStart));

    // An item continues its level's count, or starts it at the level's start.
    // It restarts the levels below it, as Word does by default: the "a." under
    // a new "2." is "a." again. Bulleted items go through it too, so they
    // restart the numbered levels below them.
    private int NextNumber(DocxListReference list)
    {
        if (!_numbersByLevelByListId.TryGetValue(list.ListId, out var numbersByLevel))
        {
            numbersByLevel = [];
            _numbersByLevelByListId[list.ListId] = numbersByLevel;
        }

        var number = numbersByLevel.TryGetValue(list.Level, out var previous)
            ? previous + 1
            : content.Lists.StartOf(list);
        numbersByLevel[list.Level] = number;

        foreach (var deeperLevel in numbersByLevel.Keys.Where(level => level > list.Level).ToList())
        {
            numbersByLevel.Remove(deeperLevel);
        }

        return number;
    }
}

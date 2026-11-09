using DocumentFormat.OpenXml.Wordprocessing;
using static kisatsingen.Services.KnowledgeFiles.Conversion.Office.OpenXmlValues;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Reads numbering.xml to tell whether a paragraph's list is bulleted or
// numbered, and the number a numbered level starts at. A paragraph's numId
// points to a w:num, which points to a w:abstractNum, whose w:lvl for the
// paragraph's level has the numFmt and the start.
//
// Decimal, letters and roman numerals are all written as decimal numbers:
// "b." and "ii." have no Markdown of their own, and the number is what a
// reference such as "punkt 2" needs.
internal sealed class DocxLists
{
    // numId 0 is how Word says a paragraph is in no list.
    private const int NoListId = 0;

    // Compared as written, since the SDK's typed format throws on a value it
    // doesn't know, and every value but these two means "numbered".
    private const string BulletFormat = "bullet";

    // Word's format for list text with no marker shown, such as an indented
    // paragraph inside a list; written as an ordinary paragraph.
    private const string NoMarkerFormat = "none";

    // Word writes a start for every level; one without counts from 1, as a reader would.
    private const int DefaultStart = 1;

    // Each list's levels. FromXml follows numId → abstractNum once, and
    // applies the list's own start overrides, so KindOf and StartOf don't have to.
    private readonly Dictionary<int, Dictionary<int, ListLevel>> _levelsByListId;

    private DocxLists(Dictionary<int, Dictionary<int, ListLevel>> levelsByListId) =>
        _levelsByListId = levelsByListId;

    // A document without numbering.xml has no lists.
    public static DocxLists FromXml(Numbering? numbering)
    {
        var levelsByAbstractListId = new Dictionary<int, Dictionary<int, ListLevel>>();
        foreach (var abstractList in numbering?.Elements<AbstractNum>() ?? [])
        {
            if (ValueOf(abstractList.AbstractNumberId) is { } abstractListId)
            {
                levelsByAbstractListId.TryAdd(abstractListId, ReadLevels(abstractList));
            }
        }

        // A list whose abstract list is missing, or defines its levels in a
        // list style (numStyleLink), has no levels here. Its paragraphs are
        // still in a list, so KindOf gives them the plainest marker.
        var levelsByListId = new Dictionary<int, Dictionary<int, ListLevel>>();
        foreach (var list in numbering?.Elements<NumberingInstance>() ?? [])
        {
            if (ValueOf(list.NumberID) is { } listId && ValueOf(list.AbstractNumId?.Val) is { } abstractListId)
            {
                var abstractLevels = levelsByAbstractListId.GetValueOrDefault(abstractListId) ?? [];
                levelsByListId.TryAdd(listId, WithStartOverrides(abstractLevels, list));
            }
        }

        return new DocxLists(levelsByListId);
    }

    // Null when the paragraph is in no list, or its level shows no marker.
    public DocxListKind? KindOf(DocxListReference list)
    {
        if (list.ListId == NoListId || !_levelsByListId.TryGetValue(list.ListId, out var levels))
        {
            return null;
        }

        if (!levels.TryGetValue(list.Level, out var level))
        {
            return DocxListKind.Bulleted;
        }

        return level.Format switch
        {
            BulletFormat => DocxListKind.Bulleted,
            NoMarkerFormat => null,
            // Every other format numbers, and a level without numFmt is decimal, Word's default.
            _ => DocxListKind.Numbered
        };
    }

    // The number the level's first item gets.
    public int StartOf(DocxListReference list) =>
        _levelsByListId.GetValueOrDefault(list.ListId)?.GetValueOrDefault(list.Level)?.Start ?? DefaultStart;

    private static Dictionary<int, ListLevel> ReadLevels(AbstractNum abstractList)
    {
        var levels = new Dictionary<int, ListLevel>();
        foreach (var level in abstractList.Elements<Level>())
        {
            if (ValueOf(level.LevelIndex) is { } levelNumber)
            {
                levels.TryAdd(levelNumber, new ListLevel(level.NumberingFormat?.Val?.InnerText, ValueOf(level.StartNumberingValue?.Val) ?? DefaultStart));
            }
        }

        return levels;
    }

    // Word restarts a list's numbering by giving it its own w:num with a
    // startOverride, so lists sharing an abstract list can start differently.
    private static Dictionary<int, ListLevel> WithStartOverrides(Dictionary<int, ListLevel> abstractLevels, NumberingInstance list)
    {
        var levels = new Dictionary<int, ListLevel>(abstractLevels);
        foreach (var levelOverride in list.Elements<LevelOverride>())
        {
            if (ValueOf(levelOverride.LevelIndex) is not { } levelNumber || !levels.TryGetValue(levelNumber, out var level))
            {
                continue;
            }

            if (ValueOf(levelOverride.StartOverrideNumberingValue?.Val) is { } start)
            {
                levels[levelNumber] = level with { Start = start };
            }
        }

        return levels;
    }

    private sealed record ListLevel(string? Format, int Start);
}

internal enum DocxListKind
{
    Bulleted,
    Numbered
}

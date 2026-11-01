using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.OfficeConversion.Docx;

// Reads numbering.xml to tell whether a paragraph's list is bulleted or
// numbered. A paragraph's numId points to a w:num, which points to a
// w:abstractNum, whose w:lvl for the paragraph's level has the numFmt.
//
// Only bulleted or numbered matters: Markdown renumbers a list itself, so
// decimal, letters and roman numerals all become "1.".
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

    // Each list's format per level. From follows numId → abstractNum once,
    // so KindOf doesn't have to.
    private readonly Dictionary<int, Dictionary<int, string?>> _formatsByLevelByListId;

    private DocxLists(Dictionary<int, Dictionary<int, string?>> formatsByLevelByListId) =>
        _formatsByLevelByListId = formatsByLevelByListId;

    // A document without numbering.xml has no lists.
    public static DocxLists From(Numbering? numbering)
    {
        var formatsByLevelByAbstractListId = new Dictionary<int, Dictionary<int, string?>>();
        foreach (var abstractList in numbering?.Elements<AbstractNum>() ?? [])
        {
            if (ValueOf(abstractList.AbstractNumberId) is { } abstractListId)
            {
                formatsByLevelByAbstractListId.TryAdd(abstractListId, ReadFormatsByLevel(abstractList));
            }
        }

        // A list whose abstract list is missing, or defines its levels in a
        // list style (numStyleLink), has no formats here. Its paragraphs are
        // still in a list, so KindOf gives them the plainest marker.
        var formatsByLevelByListId = new Dictionary<int, Dictionary<int, string?>>();
        foreach (var list in numbering?.Elements<NumberingInstance>() ?? [])
        {
            if (ValueOf(list.NumberID) is { } listId && ValueOf(list.AbstractNumId?.Val) is { } abstractListId)
            {
                formatsByLevelByListId.TryAdd(listId, formatsByLevelByAbstractListId.GetValueOrDefault(abstractListId) ?? []);
            }
        }

        return new DocxLists(formatsByLevelByListId);
    }

    // Null when the paragraph is in no list, or its level shows no marker.
    public DocxListKind? KindOf(DocxListReference list)
    {
        if (list.ListId == NoListId || !_formatsByLevelByListId.TryGetValue(list.ListId, out var formatsByLevel))
        {
            return null;
        }

        if (!formatsByLevel.TryGetValue(list.Level, out var format))
        {
            return DocxListKind.Bulleted;
        }

        return format switch
        {
            BulletFormat => DocxListKind.Bulleted,
            NoMarkerFormat => null,
            // Every other format numbers, and a level without numFmt is decimal, Word's default.
            _ => DocxListKind.Numbered,
        };
    }

    private static Dictionary<int, string?> ReadFormatsByLevel(AbstractNum abstractList)
    {
        var formatsByLevel = new Dictionary<int, string?>();
        foreach (var level in abstractList.Elements<Level>())
        {
            if (ValueOf(level.LevelIndex) is { } levelNumber)
            {
                formatsByLevel.TryAdd(levelNumber, level.NumberingFormat?.Val?.InnerText);
            }
        }

        return formatsByLevel;
    }

    // Null when absent or not a number: .Value would throw on a value the SDK can't parse.
    private static int? ValueOf(Int32Value? number) => number is { HasValue: true } ? number.Value : null;
}

internal enum DocxListKind
{
    Bulleted,
    Numbered,
}

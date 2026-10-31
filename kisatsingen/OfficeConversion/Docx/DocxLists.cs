using System.Globalization;
using System.Xml.Linq;

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
    private const string NoListId = "0";

    private const string BulletFormat = "bullet";

    // Word's format for list text with no marker shown, such as an indented
    // paragraph inside a list; written as an ordinary paragraph.
    private const string NoMarkerFormat = "none";

    private readonly Dictionary<string, string> _abstractListIdsByListId;
    private readonly Dictionary<string, Dictionary<int, string?>> _formatsByLevelByAbstractListId;

    private DocxLists(
        Dictionary<string, string> abstractListIdsByListId,
        Dictionary<string, Dictionary<int, string?>> formatsByLevelByAbstractListId)
    {
        _abstractListIdsByListId = abstractListIdsByListId;
        _formatsByLevelByAbstractListId = formatsByLevelByAbstractListId;
    }

    // A document without numbering.xml has no lists.
    public static DocxLists From(XDocument? numberingPart)
    {
        var abstractListIdsByListId = new Dictionary<string, string>(StringComparer.Ordinal);
        var formatsByLevelByAbstractListId = new Dictionary<string, Dictionary<int, string?>>(StringComparer.Ordinal);
        if (numberingPart?.Root is not { } root)
        {
            return new DocxLists(abstractListIdsByListId, formatsByLevelByAbstractListId);
        }

        // Taken from the root, so Strict and Transitional files both read.
        var w = root.Name.Namespace;

        foreach (var list in root.Elements(w + "num"))
        {
            var listId = (string?)list.Attribute(w + "numId");
            var abstractListId = (string?)list.Element(w + "abstractNumId")?.Attribute(w + "val");
            if (listId is not null && abstractListId is not null)
            {
                abstractListIdsByListId.TryAdd(listId, abstractListId);
            }
        }

        foreach (var abstractList in root.Elements(w + "abstractNum"))
        {
            if ((string?)abstractList.Attribute(w + "abstractNumId") is { } abstractListId)
            {
                formatsByLevelByAbstractListId.TryAdd(abstractListId, ReadFormatsByLevel(abstractList, w));
            }
        }

        return new DocxLists(abstractListIdsByListId, formatsByLevelByAbstractListId);
    }

    // Null when the paragraph is in no list, or its level shows no marker.
    public DocxListKind? KindOf(DocxListReference list)
    {
        if (list.ListId == NoListId || !_abstractListIdsByListId.TryGetValue(list.ListId, out var abstractListId))
        {
            return null;
        }

        // A list linked to a list style (numStyleLink) defines its levels in
        // that style, not here. The paragraph is still in a list, so it gets
        // the plainest marker.
        if (!_formatsByLevelByAbstractListId.TryGetValue(abstractListId, out var formatsByLevel)
            || !formatsByLevel.TryGetValue(list.Level, out var format))
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

    private static Dictionary<int, string?> ReadFormatsByLevel(XElement abstractList, XNamespace w)
    {
        var formatsByLevel = new Dictionary<int, string?>();
        foreach (var level in abstractList.Elements(w + "lvl"))
        {
            if (int.TryParse((string?)level.Attribute(w + "ilvl"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var levelNumber))
            {
                formatsByLevel.TryAdd(levelNumber, (string?)level.Element(w + "numFmt")?.Attribute(w + "val"));
            }
        }

        return formatsByLevel;
    }
}

internal enum DocxListKind
{
    Bulleted,
    Numbered,
}

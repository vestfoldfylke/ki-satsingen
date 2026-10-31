using System.Globalization;
using System.Xml.Linq;

namespace kisatsingen.OfficeConversion.Docx;

// What a paragraph or character style in styles.xml means for the Markdown:
// its heading level, its list, and whether it makes text bold, italic or
// struck. A style inherits what it doesn't set itself from the style it is
// based on, so every answer walks that chain.
//
// Styles are told apart by what they do, not by their ID, since the ID
// depends on Word's language: Norwegian Word writes Overskrift1 where English
// Word writes Heading1, and both set outline level 0.
internal sealed class DocxStyles
{
    // Outline levels 0–8 are heading levels 1–9; 9 marks a style as body text.
    private const int DeepestOutlineLevel = 8;

    private const string ParagraphStyleType = "paragraph";

    // Used when a style sets no outline level: Title and Subtitle never do,
    // and some tools leave it out of headings. Built-in names are written in
    // English whatever Word's language, so Overskrift1 is named "heading 1".
    private static readonly Dictionary<string, int> OutlineLevelsByBuiltInName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Title"] = 0,
        ["Subtitle"] = 1,
        ["heading 1"] = 0,
        ["heading 2"] = 1,
        ["heading 3"] = 2,
        ["heading 4"] = 3,
        ["heading 5"] = 4,
        ["heading 6"] = 5,
        ["heading 7"] = 6,
        ["heading 8"] = 7,
        ["heading 9"] = 8,
    };

    private readonly Dictionary<string, Style> _stylesById;
    private readonly string? _defaultParagraphStyleId;

    private DocxStyles(Dictionary<string, Style> stylesById, string? defaultParagraphStyleId)
    {
        _stylesById = stylesById;
        _defaultParagraphStyleId = defaultParagraphStyleId;
    }

    // A document without styles.xml has no styles, so nothing is a heading or a list.
    public static DocxStyles From(XDocument? stylesPart)
    {
        var stylesById = new Dictionary<string, Style>(StringComparer.Ordinal);
        if (stylesPart?.Root is not { } root)
        {
            return new DocxStyles(stylesById, defaultParagraphStyleId: null);
        }

        string? defaultParagraphStyleId = null;

        // Taken from the root, so Strict and Transitional files both read.
        var w = root.Name.Namespace;
        foreach (var style in root.Elements(w + "style"))
        {
            if ((string?)style.Attribute(w + "styleId") is not { } styleId)
            {
                continue;
            }

            stylesById.TryAdd(styleId, ReadStyle(style, w));
            if ((string?)style.Attribute(w + "type") == ParagraphStyleType && DocxToggle.IsOn(style.Attribute(w + "default")))
            {
                defaultParagraphStyleId ??= styleId;
            }
        }

        return new DocxStyles(stylesById, defaultParagraphStyleId);
    }

    // Word's outline level (0 for heading 1), or null when the style is no heading.
    public int? OutlineLevelOf(string? paragraphStyleId)
    {
        var outlineLevel = FirstSetAlongChain(ResolveParagraphStyleId(paragraphStyleId), style => style.OutlineLevel);
        return outlineLevel is >= 0 and <= DeepestOutlineLevel ? outlineLevel : null;
    }

    // Null when no style in the chain sets a list. A ListId of "0" means the
    // style cancels a list it would otherwise inherit.
    public DocxListReference? ListOf(string? paragraphStyleId) =>
        FirstSetAlongChain(ResolveParagraphStyleId(paragraphStyleId), style => style.List);

    // The character style overrides the paragraph style. Word actually flips a
    // toggle that both switch on (bold twice is not bold); that is rare enough
    // not to model.
    public RunFormatting FormattingOf(string? paragraphStyleId, string? characterStyleId)
    {
        bool ResolveToggle(Func<Style, bool?> toggle) =>
            FirstSetAlongChain(characterStyleId, toggle)
            ?? FirstSetAlongChain(ResolveParagraphStyleId(paragraphStyleId), toggle)
            ?? false;

        return new RunFormatting(
            IsBold: ResolveToggle(style => style.IsBold),
            IsItalic: ResolveToggle(style => style.IsItalic),
            IsStruck: ResolveToggle(style => style.IsStruck));
    }

    // A paragraph without a style of its own has the default paragraph style.
    private string? ResolveParagraphStyleId(string? paragraphStyleId) => paragraphStyleId ?? _defaultParagraphStyleId;

    // Walks from the style to the one it's based on, and so on, until one
    // sets the property.
    private T? FirstSetAlongChain<T>(string? styleId, Func<Style, T?> property) where T : struct
    {
        var visitedStyleIds = new HashSet<string>(StringComparer.Ordinal);
        while (styleId is not null && _stylesById.TryGetValue(styleId, out var style))
        {
            // Word never writes a chain that loops back on itself, but a crafted file can.
            if (visitedStyleIds.Contains(styleId))
            {
                return null;
            }

            visitedStyleIds.Add(styleId);

            if (property(style) is { } value)
            {
                return value;
            }

            styleId = style.BasedOn;
        }

        return null;
    }

    private static Style ReadStyle(XElement style, XNamespace w)
    {
        var paragraphProperties = style.Element(w + "pPr");
        var runProperties = style.Element(w + "rPr");

        return new Style(
            BasedOn: (string?)style.Element(w + "basedOn")?.Attribute(w + "val"),
            OutlineLevel: ReadOutlineLevel(style, paragraphProperties, w),
            List: ReadList(paragraphProperties, w),
            IsBold: DocxToggle.Read(runProperties?.Element(w + "b"), w),
            IsItalic: DocxToggle.Read(runProperties?.Element(w + "i"), w),
            IsStruck: DocxToggle.ReadStrike(runProperties, w));
    }

    // Kept as written, 9 for body text included: a style that sets body text
    // must stop the chain, not inherit its base style's heading level.
    private static int? ReadOutlineLevel(XElement style, XElement? paragraphProperties, XNamespace w)
    {
        if (ReadInt(paragraphProperties?.Element(w + "outlineLvl"), w) is { } outlineLevel)
        {
            return outlineLevel;
        }

        var name = (string?)style.Element(w + "name")?.Attribute(w + "val");
        return name is not null && OutlineLevelsByBuiltInName.TryGetValue(name, out var builtInOutlineLevel)
            ? builtInOutlineLevel
            : null;
    }

    private static DocxListReference? ReadList(XElement? paragraphProperties, XNamespace w)
    {
        var numberingProperties = paragraphProperties?.Element(w + "numPr");
        if ((string?)numberingProperties?.Element(w + "numId")?.Attribute(w + "val") is not { } listId)
        {
            return null;
        }

        return new DocxListReference(listId, ReadInt(numberingProperties.Element(w + "ilvl"), w) ?? 0);
    }

    private static int? ReadInt(XElement? element, XNamespace w) =>
        int.TryParse((string?)element?.Attribute(w + "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // Null where the style doesn't set the property, so it's inherited.
    private sealed record Style(
        string? BasedOn,
        int? OutlineLevel,
        DocxListReference? List,
        bool? IsBold,
        bool? IsItalic,
        bool? IsStruck);
}

// A list a paragraph is in: Word's numId, and its nesting level from 0.
// numId "0" means the paragraph is in no list, cancelling one it would inherit.
internal readonly record struct DocxListReference(string ListId, int Level);

internal readonly record struct RunFormatting(bool IsBold, bool IsItalic, bool IsStruck);

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

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

    // Word's chains are a few styles long. Following at most this many keeps
    // every lookup cheap however long a crafted chain is, and ends one that
    // loops back on itself.
    private const int MaxChainLength = 32;

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

    private readonly Dictionary<string, StyleSettings> _stylesById;
    private readonly string? _defaultParagraphStyleId;

    private DocxStyles(Dictionary<string, StyleSettings> stylesById, string? defaultParagraphStyleId)
    {
        _stylesById = stylesById;
        _defaultParagraphStyleId = defaultParagraphStyleId;
    }

    // A document without styles.xml has no styles, so nothing is a heading or a list.
    public static DocxStyles From(Styles? styles)
    {
        var stylesById = new Dictionary<string, StyleSettings>(StringComparer.Ordinal);
        string? defaultParagraphStyleId = null;
        foreach (var style in styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is not { } styleId)
            {
                continue;
            }

            stylesById.TryAdd(styleId, ReadSettings(style));
            var isParagraphStyle = style.Type is { HasValue: true } && style.Type.Value == StyleValues.Paragraph;
            if (isParagraphStyle && DocxToggle.IsOn(style.Default))
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

    // Null when no style in the chain sets a list. A ListId of 0 means the
    // style cancels a list it would otherwise inherit.
    public DocxListReference? ListOf(string? paragraphStyleId) =>
        FirstSetAlongChain(ResolveParagraphStyleId(paragraphStyleId), style => style.List);

    // The character style overrides the paragraph style. Word actually flips a
    // toggle that both switch on (bold twice is not bold); that is rare enough
    // not to model.
    //
    // Single and double strikethrough are separate toggles, so switching one
    // off leaves the other; the text is struck if either is on.
    public RunFormatting FormattingOf(string? paragraphStyleId, string? characterStyleId)
    {
        bool ResolveToggle(Func<StyleSettings, bool?> toggle) =>
            FirstSetAlongChain(characterStyleId, toggle)
            ?? FirstSetAlongChain(ResolveParagraphStyleId(paragraphStyleId), toggle)
            ?? false;

        return new RunFormatting(
            IsBold: ResolveToggle(style => style.IsBold),
            IsItalic: ResolveToggle(style => style.IsItalic),
            IsStruck: ResolveToggle(style => style.IsStruck) || ResolveToggle(style => style.IsDoubleStruck));
    }

    // A paragraph without a style of its own has the default paragraph style.
    private string? ResolveParagraphStyleId(string? paragraphStyleId) => paragraphStyleId ?? _defaultParagraphStyleId;

    // Walks from the style to the one it's based on, and so on, until one
    // sets the property.
    private T? FirstSetAlongChain<T>(string? styleId, Func<StyleSettings, T?> property) where T : struct
    {
        for (var chainLength = 0; chainLength < MaxChainLength; chainLength++)
        {
            if (styleId is null || !_stylesById.TryGetValue(styleId, out var style))
            {
                return null;
            }

            if (property(style) is { } value)
            {
                return value;
            }

            styleId = style.BasedOn;
        }

        return null;
    }

    private static StyleSettings ReadSettings(Style style)
    {
        var runProperties = style.StyleRunProperties;

        return new StyleSettings(
            BasedOn: style.BasedOn?.Val?.Value,
            OutlineLevel: ReadOutlineLevel(style),
            List: ReadList(style.StyleParagraphProperties?.NumberingProperties),
            IsBold: DocxToggle.Read(runProperties?.Bold),
            IsItalic: DocxToggle.Read(runProperties?.Italic),
            IsStruck: DocxToggle.Read(runProperties?.Strike),
            IsDoubleStruck: DocxToggle.Read(runProperties?.DoubleStrike));
    }

    // Kept as written, 9 for body text included: a style that sets body text
    // must stop the chain, not inherit its base style's heading level.
    private static int? ReadOutlineLevel(Style style)
    {
        if (ValueOf(style.StyleParagraphProperties?.OutlineLevel?.Val) is { } outlineLevel)
        {
            return outlineLevel;
        }

        var name = style.StyleName?.Val?.Value;
        return name is not null && OutlineLevelsByBuiltInName.TryGetValue(name, out var builtInOutlineLevel)
            ? builtInOutlineLevel
            : null;
    }

    private static DocxListReference? ReadList(NumberingProperties? numberingProperties)
    {
        if (numberingProperties is null || ValueOf(numberingProperties.NumberingId?.Val) is not { } listId)
        {
            return null;
        }

        return new DocxListReference(listId, ValueOf(numberingProperties.NumberingLevelReference?.Val) ?? 0);
    }

    // Null when absent or not a number: .Value would throw on a value the SDK can't parse.
    private static int? ValueOf(Int32Value? number) => number is { HasValue: true } ? number.Value : null;

    // Null where the style doesn't set the property, so it's inherited.
    private sealed record StyleSettings(
        string? BasedOn,
        int? OutlineLevel,
        DocxListReference? List,
        bool? IsBold,
        bool? IsItalic,
        bool? IsStruck,
        bool? IsDoubleStruck);
}

// A list a paragraph is in: Word's numId, and its nesting level from 0.
// numId 0 means the paragraph is in no list, cancelling one it would inherit.
internal readonly record struct DocxListReference(int ListId, int Level);

internal readonly record struct RunFormatting(bool IsBold, bool IsItalic, bool IsStruck);

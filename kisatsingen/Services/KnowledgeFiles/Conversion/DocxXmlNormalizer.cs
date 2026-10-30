using System.Xml.Linq;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Rewrites a Word XML part so MarkItDotNet understands it. Elements are matched on
// their namespace and local name, not their text, so the prefix and the attribute
// layout the writing tool chose make no difference.
internal static class DocxXmlNormalizer
{
    // Paragraph styles MarkItDotNet turns into headings. Markdown has six heading levels, so the deeper Norwegian ones share the last.
    private static readonly Dictionary<string, string> HeadingStyles = new(StringComparer.Ordinal)
    {
        ["Overskrift1"] = "Heading1",
        ["Overskrift2"] = "Heading2",
        ["Overskrift3"] = "Heading3",
        ["Overskrift4"] = "Heading4",
        ["Overskrift5"] = "Heading5",
        ["Overskrift6"] = "Heading6",
        ["Overskrift7"] = "Heading6",
        ["Overskrift8"] = "Heading6",
        ["Overskrift9"] = "Heading6",
        ["Overskrift10"] = "Heading6",

        //["Listeavsnitt"] = "Heading6", // NOTE: There's no actual replacement for this in Markdown. CAUTION: Enabling this will also replace all list items
        ["Tittel"] = "Heading1",
        ["Undertittel"] = "Heading2"
    };

    // Character styles replaced by the direct formatting they stand for: b is bold, i is italic
    private static readonly Dictionary<string, string[]> CharacterStyleFormatting = new(StringComparer.Ordinal)
    {
        ["Sterk"] = ["b"],
        ["Sterkreferanse"] = ["b"],
        ["Sterk referanse"] = ["b"],
        ["Utheving"] = ["i"],
        ["Svakutheving"] = ["i"],
        ["Svak utheving"] = ["i"],
        ["Boktittel"] = ["i"],
        ["Svakreferanse"] = ["i"],
        ["Svak referanse"] = ["i"],
        ["Sterkutheving"] = ["b", "i"],
        ["Sterk utheving"] = ["b", "i"]
    };

    private static readonly string[] QuoteStyles = ["Sitat", "Sterksitat", "Sterktsitat", "Sterk sitat", "Sterkt sitat"];

    // Word has single and double strikethrough. Markdown only has one.
    private static readonly string[] StrikeElements = ["strike", "dstrike"];

    // Values that switch off a Word toggle property, as in <w:strike w:val="0"/>
    private static readonly string[] SwitchedOffValues = ["0", "false", "off"];

    // Returns whether the part was changed
    public static bool Normalize(XDocument part)
    {
        if (part.Root is null)
        {
            return false;
        }

        var w = part.Root.Name.Namespace;
        var isStyleChanged = NormalizeHeadingStyles(part);
        var isCharacterStyleChanged = NormalizeCharacterStyles(part, w);
        var isParagraphChanged = false;

        // Quotes and strikethrough have no style MarkItDotNet understands, so their markers are written into the text itself
        foreach (var paragraph in part.Descendants(w + "p").ToList())
        {
            // Strikethrough first, so a struck quote becomes "> *~~text~~*" and not "~~> *text*~~"
            var isStruck = NormalizeStrikethrough(paragraph);
            var isQuote = NormalizeQuote(paragraph);
            isParagraphChanged = isParagraphChanged || isStruck || isQuote;
        }

        return isStyleChanged || isCharacterStyleChanged || isParagraphChanged;
    }

    // Covers pStyle in document.xml and the basedOn, next and link references in styles.xml
    private static bool NormalizeHeadingStyles(XDocument part)
    {
        var isChanged = false;
        foreach (var val in part.Descendants().Attributes().Where(attribute => attribute.Name.LocalName == "val"))
        {
            if (!HeadingStyles.TryGetValue(val.Value, out var headingStyle))
            {
                continue;
            }

            val.Value = headingStyle;
            isChanged = true;
        }

        return isChanged;
    }

    private static bool NormalizeCharacterStyles(XDocument part, XNamespace w)
    {
        var isChanged = false;
        foreach (var rStyle in part.Descendants(w + "rStyle").ToList())
        {
            var styleName = ValueOf(rStyle);
            if (styleName is null || !CharacterStyleFormatting.TryGetValue(styleName, out var formatting))
            {
                continue;
            }

            rStyle.ReplaceWith(formatting.Select(name => new XElement(w + name)));
            isChanged = true;
        }

        return isChanged;
    }

    // Wraps each stretch of struck runs, so a paragraph with one struck word only has that word struck through
    private static bool NormalizeStrikethrough(XElement paragraph)
    {
        var isChanged = false;
        var struckTextNodes = new List<XElement>();

        foreach (var run in OwnElements(paragraph, "r"))
        {
            var textNodes = run.Elements(paragraph.Name.Namespace + "t").ToList();

            // Runs without text, like tabs, breaks and field codes, neither start nor end a stretch
            if (textNodes.Count == 0)
            {
                continue;
            }

            if (!RemoveStrikes(run))
            {
                isChanged = WrapText(struckTextNodes, "~~", "~~") || isChanged;
                struckTextNodes.Clear();
                continue;
            }

            isChanged = true;
            struckTextNodes.AddRange(textNodes);
        }

        return WrapText(struckTextNodes, "~~", "~~") || isChanged;
    }

    // Only the strike elements go, so bold, italic and other formatting survive. Returns whether the run was struck.
    private static bool RemoveStrikes(XElement run)
    {
        var strikes = (FindChild(run, "rPr")?.Elements() ?? [])
            .Where(element => StrikeElements.Contains(element.Name.LocalName))
            .ToList();

        strikes.Remove();
        return strikes.Any(IsSwitchedOn);
    }

    private static bool NormalizeQuote(XElement paragraph)
    {
        var pStyle = FindChild(FindChild(paragraph, "pPr"), "pStyle");
        var styleName = pStyle is null ? null : ValueOf(pStyle);
        if (pStyle is null || styleName is null || !QuoteStyles.Contains(styleName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var emphasis = styleName.Contains("sterk", StringComparison.OrdinalIgnoreCase) ? "**" : "*";

        // Remove the pStyle element so MarkItDotNet treats this as regular text
        pStyle.Remove();

        WrapText(OwnElements(paragraph, "t").ToList(), "> " + emphasis, emphasis);
        return true;
    }

    // Word splits text into runs at every edit or spell check, so the markers go before the
    // first text and after the last text, not around one run. They go inside surrounding
    // whitespace, since Markdown ignores "~~ text ~~" and "* text *".
    private static bool WrapText(List<XElement> textNodes, string prefix, string suffix)
    {
        if (textNodes.All(textNode => string.IsNullOrWhiteSpace(textNode.Value)))
        {
            return false;
        }

        var first = textNodes.First(textNode => !string.IsNullOrWhiteSpace(textNode.Value));
        var leadingWhitespace = first.Value[..^first.Value.TrimStart().Length];
        first.Value = leadingWhitespace + prefix + first.Value.TrimStart();

        var last = textNodes.Last(textNode => !string.IsNullOrWhiteSpace(textNode.Value));
        var trimmed = last.Value.TrimEnd();
        last.Value = trimmed + suffix + last.Value[trimmed.Length..];
        return true;
    }

    // Text boxes nest whole paragraphs inside a run, and DrawingML shapes have their own
    // a:p and a:t. Only elements whose nearest Word paragraph is this one belong to it.
    private static IEnumerable<XElement> OwnElements(XElement paragraph, string localName) =>
        paragraph.Descendants(paragraph.Name.Namespace + localName)
            .Where(element => element.Ancestors(paragraph.Name).First() == paragraph);

    private static bool IsSwitchedOn(XElement toggle)
    {
        var value = ValueOf(toggle);
        return value is null || !SwitchedOffValues.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ValueOf(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "val")?.Value;

    private static XElement? FindChild(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
}

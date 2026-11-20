namespace kisatsingen.Services.Presentations;

// The room a placeholder gives its text, and the size the template sets.
internal sealed record TextBox(double WidthPoints, double HeightPoints, double FontSizePoints);

// Whether text fits its box at the template's size, and if not, the size it
// does fit at. The template turns autofit off, and PowerPoint only applies
// autofit when text is edited, so without this a long quote at the template's
// 50 pt runs off the slide.
//
// An estimate from character counts, with no font measuring: an average
// glyph width and line height for the template's Calibri, rounded up so a
// borderline text shrinks rather than overflows. Wide glyphs (W, M) in bulk
// can still overflow a little.
internal static class TextFit
{
    private const double GlyphWidthEm = 0.52;
    private const double LineHeightEm = 1.22;
    private const double ShrinkStep = 0.9;

    // Text is never shrunk below this share of the template's size, nor below
    // MinFontSizePoints: smaller is unreadable from the back of a room. Text
    // too long even then is left to overflow; the validator's limits are what
    // keep that from happening.
    private const double MinScale = 0.4;
    private const double MinFontSizePoints = 10;

    // In hundredths of a point, as DrawingML writes sizes. Null when the text
    // fits at the template's size, which then stays the layout's to decide.
    public static int? FontSizeToFit(IReadOnlyList<string> paragraphs, TextBox box)
    {
        if (Fits(paragraphs, box, box.FontSizePoints))
        {
            return null;
        }

        var floor = Math.Max(box.FontSizePoints * MinScale, MinFontSizePoints);
        var size = box.FontSizePoints;
        while (size > floor)
        {
            size = Math.Max(floor, size * ShrinkStep);
            if (Fits(paragraphs, box, Math.Floor(size)))
            {
                break;
            }
        }

        return (int)Math.Floor(size) * 100;
    }

    private static bool Fits(IReadOnlyList<string> paragraphs, TextBox box, double fontSize)
    {
        var charactersPerLine = (int)Math.Floor(box.WidthPoints / (fontSize * GlyphWidthEm));
        if (charactersPerLine < 1)
        {
            return false;
        }

        var lines = paragraphs.Sum(paragraph => CountLines(paragraph, charactersPerLine));
        return lines * fontSize * LineHeightEm <= box.HeightPoints;
    }

    // Greedy word wrap, as PowerPoint does; a word longer than a line is
    // broken across lines. An empty paragraph still takes a line.
    internal static int CountLines(string paragraph, int charactersPerLine)
    {
        var lines = 1;
        var used = 0;
        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var needed = used == 0 ? word.Length : used + 1 + word.Length;
            if (needed <= charactersPerLine)
            {
                used = needed;
                continue;
            }

            if (used > 0)
            {
                lines++;
            }

            lines += (word.Length - 1) / charactersPerLine;
            used = (word.Length - 1) % charactersPerLine + 1;
        }

        return lines;
    }
}

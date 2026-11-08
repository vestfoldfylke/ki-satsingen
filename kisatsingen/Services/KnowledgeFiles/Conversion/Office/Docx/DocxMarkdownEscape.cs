using System.Text;
using System.Text.RegularExpressions;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Escapes the document's own text, so text that looks like Markdown stays
// text: "[klammer](ikke-lenke)" is no link, "<b>" is no HTML, and "- punkt"
// at a line's start is no list.
//
// Characters are escaped only where Markdown would read them, so ordinary
// text keeps its look: an underscore inside a word (snake_case) and a
// backslash before a letter (C:\Users) are left alone.
internal static partial class DocxMarkdownEscape
{
    private const char Backslash = '\\';

    // Characters that start or end inline Markdown wherever they are: emphasis,
    // code, links, GFM strikethrough, and $ for the math DocxMath writes.
    private const string AlwaysEscaped = "`*[]~$";

    // What a backslash escapes in CommonMark.
    private const string AsciiPunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    public static string Inline(string text)
    {
        var escaped = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var previous = index > 0 ? text[index - 1] : (char?)null;
            var next = index + 1 < text.Length ? text[index + 1] : (char?)null;

            if (IsEscapedAt(text, index, character, previous, next))
            {
                escaped.Append(Backslash);
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    // A line starting with # would become a heading, and DocumentOutline makes
    // sections from headings; one starting with > would become a quote, one
    // starting with "- ", "+ " or "1. " a list item. A line of only = or -
    // under text would make that text a heading.
    public static string BlockStart(string line)
    {
        if (OrderedListStart().Match(line) is { Success: true } listStart)
        {
            return line.Insert(listStart.Groups["delimiter"].Index, Backslash.ToString());
        }

        var isBlockStart = line.StartsWith('#') || line.StartsWith('>') ||
            BulletListStart().IsMatch(line) ||
            line.All(character => character == '=') || line.All(character => character == '-');

        return isBlockStart
            ? $"{Backslash}{line}"
            : line;
    }

    private static bool IsEscapedAt(string text, int index, char character, char? previous, char? next) => character switch
    {
        _ when AlwaysEscaped.Contains(character) => true,
        // Before punctuation it would escape it, and at the end it could make a line break.
        Backslash => next is not { } after || AsciiPunctuation.Contains(after),
        // Inside a word an underscore can't open or close emphasis.
        '_' => !(previous is { } before && char.IsLetterOrDigit(before) && next is { } after && char.IsLetterOrDigit(after)),
        // Only an HTML tag, comment or autolink starts with < followed by these.
        '<' => next is { } after && (char.IsAsciiLetter(after) || after is '/' or '!' or '?'),
        '&' => EntityStart().IsMatch(text.AsSpan(index)),
        _ => false
    };

    [GeneratedRegex(@"^\d{1,9}(?<delimiter>[.)])(\s|$)")]
    private static partial Regex OrderedListStart();

    [GeneratedRegex(@"^[-+](\s|$)")]
    private static partial Regex BulletListStart();

    [GeneratedRegex("^&(#[0-9]+|#[xX][0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);")]
    private static partial Regex EntityStart();
}

using System.Globalization;

namespace kisatsingen.Services;

// Cutting by char can split a surrogate pair (most emoji), leaving half of it
// behind. That half is not valid UTF-16, and Postgres, JSON and the browser all
// refuse or mangle it, so every cut of user or document text goes through here.
//
// It cuts between text elements (what a reader sees as one character), so an
// emoji built from several (a family, a skin tone) or a letter with a
// combining accent is kept whole or dropped whole, never split into pieces.
internal static class TextTruncation
{
    // At most maxLength chars, fewer when the cut would fall inside a text
    // element. Walks only up to the cut, so a long text costs no more than a
    // short one.
    public static string Prefix(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = 0;
        while (cut < maxLength)
        {
            var next = cut + StringInfo.GetNextTextElementLength(text, cut);
            if (next > maxLength)
            {
                break;
            }

            cut = next;
        }

        return text[..cut];
    }
}

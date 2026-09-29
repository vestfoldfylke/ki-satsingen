namespace kisatsingen.Services;

// Cutting by char can split a surrogate pair (most emoji), leaving half of it
// behind. That half is not valid UTF-16, and Postgres, JSON and the browser all
// refuse or mangle it, so every cut of user or document text goes through here.
//
// Only validity is guaranteed. An emoji built from several characters (a
// family, a skin tone) can still be cut into smaller valid ones, which is fine
// for titles and excerpts and not worth text-element handling.
internal static class TextTruncation
{
    // At most maxLength chars, one shorter when the cut would fall inside a
    // surrogate pair.
    public static string Prefix(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        // Only the high half can be the last char kept with its partner cut
        // off; a low half at the cut means the pair is already complete.
        var cut = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..cut];
    }
}

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

internal static class DocxMarkdownLinks
{
    // Text is inline Markdown, such as "**Søknad**".
    public static string Link(string text, string url) => $"[{EscapeText(text)}]({EscapeUrl(url)})";

    // A bracket in the text would end it early.
    private static string EscapeText(string text) => text
        .Replace("[", "\\[")
        .Replace("]", "\\]");

    // A space or a parenthesis would end the address early, and an angle
    // bracket would start one written as <...>.
    private static string EscapeUrl(string url) => url
        .Replace(" ", "%20")
        .Replace("(", "%28")
        .Replace(")", "%29")
        .Replace("<", "%3C")
        .Replace(">", "%3E");
}

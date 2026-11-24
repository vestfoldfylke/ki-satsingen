using System.Xml;

namespace kisatsingen.Services.GeneratedFiles;

// Whether text can be written into an Office file's XML as it is. Line breaks
// and tabs are allowed by XML; the builders keep them as typed.
internal static class XmlText
{
    public static bool IsValid(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (!XmlConvert.IsXmlChar(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}

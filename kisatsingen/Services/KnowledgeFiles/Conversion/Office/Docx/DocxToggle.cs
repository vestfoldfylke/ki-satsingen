using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Word's on/off properties, such as <w:b/> for bold. A toggle element without
// w:val is on; w:val="0", "false" or "off" switches it off, which a style uses
// to cancel bold it would inherit.
//
// .Value throws on a value the SDK can't parse, so HasValue is checked first.
internal static class DocxToggle
{
    // Null when the element is absent, so the property is inherited. A value
    // the SDK can't parse counts as on, as the element's presence does.
    public static bool? Read(OnOffType? toggle)
    {
        if (toggle is null)
        {
            return null;
        }

        var value = toggle.Val;
        return value is null || !value.HasValue || value.Value;
    }

    // For on/off attributes such as w:default="1", which are off when absent or unparseable.
    public static bool IsOn(OnOffValue? attribute) => attribute is { HasValue: true, Value: true };
}

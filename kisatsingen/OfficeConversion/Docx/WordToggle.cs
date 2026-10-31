using System.Xml.Linq;

namespace kisatsingen.OfficeConversion.Docx;

// Word's on/off properties, such as <w:b/> for bold. A toggle element without
// w:val is on; w:val="0", "false" or "off" switches it off, which a style uses
// to cancel bold it would inherit.
internal static class WordToggle
{
    private static readonly string[] SwitchedOffValues = ["0", "false", "off"];

    // Null when the element is absent, so the property is inherited.
    public static bool? Read(XElement? toggle, XNamespace w) =>
        toggle is null ? null : !IsSwitchedOff(toggle.Attribute(w + "val"));

    // Single and double strikethrough both mean the text is struck.
    public static bool? ReadStrike(XElement? runProperties, XNamespace w)
    {
        var strike = Read(runProperties?.Element(w + "strike"), w);
        var doubleStrike = Read(runProperties?.Element(w + "dstrike"), w);
        if (strike is null && doubleStrike is null)
        {
            return null;
        }

        return strike == true || doubleStrike == true;
    }

    // For on/off attributes such as w:default="1", which are off when absent.
    public static bool IsOn(XAttribute? attribute) => attribute is not null && !IsSwitchedOff(attribute);

    private static bool IsSwitchedOff(XAttribute? valAttribute) =>
        valAttribute is not null && SwitchedOffValues.Contains(valAttribute.Value, StringComparer.OrdinalIgnoreCase);
}

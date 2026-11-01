using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace kisatsingen.OfficeConversion;

// Opens Office files and loads their parts with the limits an upload needs,
// which the Open XML SDK leaves off or doesn't have. Every part a converter
// reads must be loaded through RootOf: the SDK's own getters, such as
// MainDocumentPart.Document, load without these checks.
internal static class CheckedParts
{
    // Real documents nest under 50 deep, even with tables in tables. Loading a
    // part takes time quadratic in its depth: 20,000 deep took 26 seconds.
    private const int MaxDepth = 256;

    // A loaded part takes about 30 times its XML's size in memory, most of it
    // per element. A million elements is a few hundred MB; a 300-page
    // document is around 600,000.
    private const int MaxElementsPerPart = 1_000_000;

    // Bounds what the element limit can't, such as one huge text node.
    // Fifty million characters is several times the largest real document.
    private const int MaxCharactersPerPart = 50_000_000;

    // A DTD can expand entities to gigabytes, and nothing Office writes has one.
    private static readonly XmlReaderSettings CheckReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharactersPerPart,
    };

    // A new instance each time, since OpenSettings is mutable. Alternate
    // content keeps only the branch Office 2019 would show; without this, the
    // SDK keeps both, and a text box comes out twice.
    public static OpenSettings OpenSettings => new()
    {
        MaxCharactersInPart = MaxCharactersPerPart,
        MarkupCompatibilityProcessSettings = new MarkupCompatibilityProcessSettings(
            MarkupCompatibilityProcessMode.ProcessLoadedPartsOnly,
            FileFormatVersions.Office2019),
    };

    // The part's root element, or null when there is no such part or its root
    // isn't a T. Throws XmlException when the part is too deep or too large.
    public static T? RootOf<T>(OpenXmlPart? part) where T : OpenXmlPartRootElement
    {
        if (part is null)
        {
            return null;
        }

        // A streaming pass first: it reads at XmlReader's speed and memory,
        // and refuses a part before the SDK spends minutes or gigabytes on it.
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        using (var reader = XmlReader.Create(stream, CheckReaderSettings))
        {
            RejectTooDeepOrTooLarge(reader);
        }

        return part.RootElement as T;
    }

    private static void RejectTooDeepOrTooLarge(XmlReader reader)
    {
        var elementCount = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Depth > MaxDepth)
            {
                throw new XmlException($"The part nests elements more than {MaxDepth} deep.");
            }

            elementCount++;
            if (elementCount > MaxElementsPerPart)
            {
                throw new XmlException($"The part has more than {MaxElementsPerPart} elements.");
            }
        }
    }
}

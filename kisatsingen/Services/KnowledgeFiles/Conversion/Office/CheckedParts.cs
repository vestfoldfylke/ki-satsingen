using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office;

// Opens Office files and loads their parts with the limits an upload needs,
// which the Open XML SDK leaves off or doesn't have. Every part a converter
// reads must be loaded through RootOf: the SDK's own getters, such as
// MainDocumentPart.Document, load without these checks.
//
// One instance per file, since the element limit counts every part the file
// loads.
internal sealed class CheckedParts(CancellationToken ct)
{
    // Real documents nest under 50 deep, even with tables in tables. Loading a
    // part takes time quadratic in its depth: 20,000 deep took 26 seconds.
    private const int MaxDepth = 256;

    // A loaded element takes a few hundred bytes. All of a file's parts are
    // loaded at once, so the limit is for the file, not each part: per part,
    // five parts near the limit took about a gigabyte. A 300-page document is
    // around 600,000 elements in all.
    private const int MaxElementsPerFile = 1_000_000;

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

    private int _elementsLeft = MaxElementsPerFile;

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
    // isn't a T. Throws XmlException when the part is too deep, or the file's
    // parts together have too many elements.
    //
    // ct is checked per element of the streaming pass and once before the SDK
    // loads the part. That loading can't be cancelled; the limits keep it to
    // about a second.
    public T? RootOf<T>(OpenXmlPart? part) where T : OpenXmlPartRootElement
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

        ct.ThrowIfCancellationRequested();
        return part.RootElement as T;
    }

    private void RejectTooDeepOrTooLarge(XmlReader reader)
    {
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            if (reader.Depth > MaxDepth)
            {
                throw new XmlException($"The part nests elements more than {MaxDepth} deep.");
            }

            _elementsLeft--;
            if (_elementsLeft < 0)
            {
                throw new XmlException($"The file's parts together have more than {MaxElementsPerFile} elements.");
            }
        }
    }
}

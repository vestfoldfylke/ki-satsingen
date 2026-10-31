using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace kisatsingen.OfficeConversion;

// A .docx, .pptx or .xlsx file: a zip of XML parts that point to each other
// through relationships. Parts are always found through those relationships,
// never by file name, since some tools name the main part document2.xml.
//
// Malformed input throws: InvalidDataException for a broken zip, XmlException
// for a broken part. A converter catches both where it starts.
public sealed class OpenXmlPackage : IDisposable
{
    // The relationships of a part are in a sibling _rels folder; the package
    // root's own relationships, which lead to the main part, are in _rels/.rels.
    private const string PackageRoot = "";

    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    // A DTD can expand entities to gigabytes, and nothing Office writes has one.
    private static readonly XmlReaderSettings PartReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private readonly ZipArchive _archive;

    // Part names are case-insensitive in Office files, zip entry names are not.
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByPartName;

    private OpenXmlPackage(ZipArchive archive)
    {
        _archive = archive;
        _entriesByPartName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            _entriesByPartName.TryAdd(entry.FullName, entry);
        }
    }

    // Throws InvalidDataException when the stream is not a zip archive.
    public static OpenXmlPackage Open(Stream stream) =>
        new(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true));

    // ZipArchive never reads past an entry's declared size, even when the size
    // is forged, so the declared sizes bound what a converter can unpack.
    public bool ExceedsUncompressedBytes(long maxUncompressedBytes)
    {
        // Summed with an early exit, so huge declared sizes cannot overflow the total
        long totalBytes = 0;
        foreach (var entry in _archive.Entries)
        {
            totalBytes += entry.Length;
            if (totalBytes > maxUncompressedBytes)
            {
                return true;
            }
        }

        return false;
    }

    // The main part, such as word/document.xml: what the package root's
    // officeDocument relationship points to.
    public string? FindMainPart() => FindRelatedPart(PackageRoot, RelationshipType.OfficeDocument);

    // The first part of this type that partName points to, or null when it points to none.
    public string? FindRelatedPart(string partName, RelationshipType type) =>
        RelationshipsOf(partName)
            .Where(relationship => !relationship.IsExternal && type.Matches(relationship.Type))
            .Select(relationship => relationship.Target)
            .FirstOrDefault(_entriesByPartName.ContainsKey);

    // Internal targets are resolved to part names; external ones, such as a
    // hyperlink's address, are kept as written.
    public IReadOnlyList<PackageRelationship> RelationshipsOf(string partName)
    {
        var relationships = LoadPart(RelationshipsPartName(partName));
        if (relationships?.Root is null)
        {
            return [];
        }

        return relationships.Root.Elements(RelationshipsNamespace + "Relationship")
            .Select(relationship => ReadRelationship(partName, relationship))
            .OfType<PackageRelationship>()
            .ToList();
    }

    // Null when the package has no such part.
    public XDocument? LoadPart(string partName)
    {
        if (!_entriesByPartName.TryGetValue(partName, out var entry))
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, PartReaderSettings);

        // Word's text can be nothing but spaces, which only survives if whitespace is kept.
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public void Dispose() => _archive.Dispose();

    private static PackageRelationship? ReadRelationship(string sourcePartName, XElement relationship)
    {
        var id = (string?)relationship.Attribute("Id");
        var type = (string?)relationship.Attribute("Type");
        var target = (string?)relationship.Attribute("Target");
        if (id is null || type is null || target is null)
        {
            return null;
        }

        var isExternal = string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
        return isExternal
            ? new PackageRelationship(id, type, target, IsExternal: true)
            : new PackageRelationship(id, type, ResolveTarget(sourcePartName, target), IsExternal: false);
    }

    // word/document.xml → word/_rels/document.xml.rels
    private static string RelationshipsPartName(string partName)
    {
        var folderEnd = partName.LastIndexOf('/') + 1;
        return $"{partName[..folderEnd]}_rels/{partName[folderEnd..]}.rels";
    }

    // A target is relative to the folder of the part that points to it,
    // unless it starts with a slash, and may climb out of it with "..".
    private static string ResolveTarget(string sourcePartName, string target)
    {
        var path = Uri.UnescapeDataString(target);
        var folder = path.StartsWith('/') ? "" : sourcePartName[..(sourcePartName.LastIndexOf('/') + 1)];

        var segments = new List<string>();
        foreach (var segment in (folder + path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }
}

// Target is a part name for an internal relationship, an address for an external one.
public sealed record PackageRelationship(string Id, string Type, string Target, bool IsExternal);

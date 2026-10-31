using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace kisatsingen.OfficeConversion;

// A .docx, .pptx or .xlsx file: a zip of XML parts that point to each other
// through relationships. Parts are always found through those relationships,
// never by file name, since some tools name the main part document2.xml.
//
// Throws InvalidDataException for a broken zip and XmlException for a broken part.
public sealed class OpenXmlPackage : IDisposable
{
    // The package root has the empty part name, so RelationshipsPartName maps it to _rels/.rels.
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

    // The first part of this type that partName points to and the package
    // contains, or null when there is none.
    public string? FindRelatedPart(string partName, RelationshipType type) =>
        RelationshipsOf(partName)
            .Where(relationship => !relationship.IsExternal && type.Matches(relationship.TypeUri))
            .Select(relationship => relationship.Target)
            .FirstOrDefault(target => _entriesByPartName.ContainsKey(target));

    // Internal targets are resolved to part names; external ones, such as a
    // hyperlink's address, are kept as written.
    public IReadOnlyList<PackageRelationship> RelationshipsOf(string partName)
    {
        var relationshipsPart = LoadPart(RelationshipsPartName(partName));
        if (relationshipsPart?.Root is null)
        {
            return [];
        }

        return relationshipsPart.Root.Elements(RelationshipsNamespace + "Relationship")
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
        return XDocument.Load(reader);
    }

    public void Dispose() => _archive.Dispose();

    // Null when the relationship lacks an Id, Type or Target.
    private static PackageRelationship? ReadRelationship(string sourcePartName, XElement relationship)
    {
        var id = (string?)relationship.Attribute("Id");
        var typeUri = (string?)relationship.Attribute("Type");
        var target = (string?)relationship.Attribute("Target");
        if (id is null || typeUri is null || target is null)
        {
            return null;
        }

        var isExternal = string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
        var resolvedTarget = isExternal ? target : ResolveTarget(sourcePartName, target);
        return new PackageRelationship(id, typeUri, resolvedTarget, isExternal);
    }

    // A part's relationships are in a _rels folder beside it:
    // word/document.xml → word/_rels/document.xml.rels.
    private static string RelationshipsPartName(string partName)
    {
        var folder = FolderOf(partName);
        return $"{folder}_rels/{partName[folder.Length..]}.rels";
    }

    // A target is relative to the folder of the part that points to it,
    // unless it starts with a slash, and may climb out of it with "..".
    private static string ResolveTarget(string sourcePartName, string target)
    {
        var path = Uri.UnescapeDataString(target);
        var folder = path.StartsWith('/') ? "" : FolderOf(sourcePartName);

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

    // "word/document.xml" → "word/", and "" for a part at the package root.
    private static string FolderOf(string partName) => partName[..(partName.LastIndexOf('/') + 1)];
}

// Target is a part name for an internal relationship, an address for an external one.
public sealed record PackageRelationship(string Id, string TypeUri, string Target, bool IsExternal);

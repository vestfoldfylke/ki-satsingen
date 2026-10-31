using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace kisatsingen.Tests.OfficeConversion;

// Builds an Office package from raw parts, so tests control exactly what a
// writing tool would put in it: part names, relationship targets, namespaces.
internal static class TestPackage
{
    public const string TransitionalOfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string StrictOfficeDocument = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    public const string TransitionalStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    public const string TransitionalHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public sealed record Relationship(string Id, string Type, string Target, bool IsExternal = false);

    public static byte[] Zip(params (string Name, string Content)[] parts)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static string Relationships(params Relationship[] relationships) =>
        $"""
         <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
         <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{string.Concat(relationships.Select(RelationshipElement))}</Relationships>
         """;

    // Rewrites the uncompressed size an entry declares in the central directory,
    // as a zip bomb would, without having to build gigabytes of content.
    // From Rune's TestDocx on the docx-parser branch.
    public static byte[] WithDeclaredUncompressedSizes(byte[] package, params (string EntryName, uint Size)[] sizes)
    {
        const uint centralDirectoryHeaderSignature = 0x02014b50;
        const int uncompressedSizeOffset = 24;
        const int fileNameLengthOffset = 28;
        const int fileNameOffset = 46;

        var forged = package.ToArray();
        for (var position = 0; position + fileNameOffset <= forged.Length; position++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(forged.AsSpan(position)) != centralDirectoryHeaderSignature)
            {
                continue;
            }

            var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(forged.AsSpan(position + fileNameLengthOffset));
            var fileName = Encoding.UTF8.GetString(forged, position + fileNameOffset, fileNameLength);
            foreach (var (_, size) in sizes.Where(forgedSize => forgedSize.EntryName == fileName))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(position + uncompressedSizeOffset), size);
            }
        }

        return forged;
    }

    private static string RelationshipElement(Relationship relationship) =>
        $"<Relationship Id=\"{relationship.Id}\" Type=\"{relationship.Type}\" Target=\"{relationship.Target}\"{(relationship.IsExternal ? " TargetMode=\"External\"" : "")}/>";
}

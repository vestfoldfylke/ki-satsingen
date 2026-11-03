using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office;

// Builds an Office package from raw parts, so tests control exactly what a
// writing tool would put in it: part names, relationship targets, namespaces.
internal static class TestPackage
{
    public const string TransitionalOfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    // A relationship as written in a .rels part.
    public sealed record Relationship(string Id, string TypeUri, string Target, bool IsExternal = false);

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

    // A zip of one stored entry whose central directory declares its size in
    // a ZIP64 extra field, as declaredSize, which can be negative. Built by
    // hand, since .NET won't write a size it knows is wrong. The CRC is left
    // at 0: tests using this check the declared size, never the content.
    public static byte[] WithZip64DeclaredSize(string name, string content, long declaredSize)
    {
        const ushort zip64Version = 45;
        const ushort storedMethod = 0;
        const ushort zip64ExtraFieldId = 1;
        const ushort zip64ExtraFieldSize = 8;
        const uint sizeInZip64ExtraField = 0xFFFFFFFF;

        var nameBytes = Encoding.ASCII.GetBytes(name);
        var data = Encoding.UTF8.GetBytes(content);
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        // Local file header, then the entry's data.
        writer.Write(0x04034b50u);
        writer.Write(zip64Version);
        writer.Write((ushort)0);
        writer.Write(storedMethod);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)data.Length);
        writer.Write((uint)data.Length);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);
        writer.Write(data);

        // Central directory: the size field says "see ZIP64", and the extra field holds the forged size.
        var centralDirectoryStart = buffer.Position;
        writer.Write(0x02014b50u);
        writer.Write(zip64Version);
        writer.Write(zip64Version);
        writer.Write((ushort)0);
        writer.Write(storedMethod);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)data.Length);
        writer.Write(sizeInZip64ExtraField);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)(2 + 2 + zip64ExtraFieldSize));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(nameBytes);
        writer.Write(zip64ExtraFieldId);
        writer.Write(zip64ExtraFieldSize);
        writer.Write(declaredSize);
        var centralDirectoryEnd = buffer.Position;

        // End of central directory: one entry.
        writer.Write(0x06054b50u);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)(centralDirectoryEnd - centralDirectoryStart));
        writer.Write((uint)centralDirectoryStart);
        writer.Write((ushort)0);

        writer.Flush();
        return buffer.ToArray();
    }

    public static string Relationships(params Relationship[] relationships) =>
        $"""
         <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
         <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{string.Concat(relationships.Select(RelationshipElement))}</Relationships>
         """;

    // Rewrites the uncompressed size an entry declares in the central directory,
    // as a zip bomb would, without having to build gigabytes of content.
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
        $"<Relationship Id=\"{relationship.Id}\" Type=\"{relationship.TypeUri}\" Target=\"{relationship.Target}\"{(relationship.IsExternal ? " TargetMode=\"External\"" : "")}/>";
}

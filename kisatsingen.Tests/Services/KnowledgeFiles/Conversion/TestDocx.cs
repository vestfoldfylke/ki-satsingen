using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

// Builds the smallest .docx Word and OpenXml accept, with the body written as
// raw WordprocessingML. Raw XML rather than the OpenXml SDK, because the
// converter matches on the exact text of the XML (localized style names, and
// whether an element is written "<x/>" or "<x />").
internal static class TestDocx
{
    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string RelationshipsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    public static byte[] WithBody(string bodyXml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypesXml);
            AddEntry(archive, "_rels/.rels", RelationshipsXml);
            AddEntry(archive, "word/document.xml", DocumentXml(bodyXml));
        }

        return buffer.ToArray();
    }

    // Rewrites the uncompressed size an entry declares in the central directory,
    // as a zip bomb would, without having to build gigabytes of content.
    public static byte[] WithDeclaredUncompressedSizes(byte[] docx, params (string EntryName, uint Size)[] sizes)
    {
        const uint centralDirectoryHeaderSignature = 0x02014b50;
        const int uncompressedSizeOffset = 24;
        const int fileNameLengthOffset = 28;
        const int fileNameOffset = 46;

        var forged = docx.ToArray();
        for (var position = 0; position + fileNameOffset <= forged.Length; position++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(forged.AsSpan(position)) != centralDirectoryHeaderSignature)
            {
                continue;
            }

            var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(forged.AsSpan(position + fileNameLengthOffset));
            var fileName = Encoding.UTF8.GetString(forged, position + fileNameOffset, fileNameLength);
            foreach (var (entryName, size) in sizes.Where(forgedSize => forgedSize.EntryName == fileName))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(position + uncompressedSizeOffset), size);
            }
        }

        return forged;
    }

    public static byte[] WithParagraphs(params string[] paragraphs) =>
        WithBody(string.Concat(paragraphs.Select(Paragraph)));

    public static string StyledParagraph(string style, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    public static string StyledRun(string runStyle, string text) =>
        $"<w:p><w:r><w:rPr><w:rStyle w:val=\"{runStyle}\"/></w:rPr><w:t>{text}</w:t></w:r></w:p>";

    // Word marks a struck paragraph on its paragraph mark as well as on the run.
    public static string StruckParagraph(string text) =>
        $"<w:p><w:pPr><w:rPr><w:strike/></w:rPr></w:pPr><w:r><w:rPr><w:strike/></w:rPr><w:t>{text}</w:t></w:r></w:p>";

    private static string Paragraph(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string DocumentXml(string bodyXml) =>
        $"""
         <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
         <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{bodyXml}</w:body></w:document>
         """;

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}

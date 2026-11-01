using static kisatsingen.Tests.OfficeConversion.TestPackage;

namespace kisatsingen.Tests.OfficeConversion.Docx;

// Builds the smallest .docx the Open XML SDK accepts, with the body written as
// raw WordprocessingML, so tests control exactly what Word or another tool
// would write: namespace prefixes, nesting, element order.
internal static class TestDocx
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    public static byte[] WithBody(string bodyXml) =>
        WithDocumentXml($"<w:document xmlns:w=\"{WordNamespace}\"><w:body>{bodyXml}</w:body></w:document>");

    public static byte[] WithDocumentXml(string documentXml) =>
        Zip(
            ("[Content_Types].xml", ContentTypesXml),
            ("_rels/.rels", Relationships(new Relationship("rId1", TransitionalOfficeDocument, "word/document.xml"))),
            ("word/document.xml", documentXml));
}

using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.TestPackage;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// Builds the smallest .docx the Open XML SDK accepts, with the body and the
// optional parts written as raw WordprocessingML, so tests control exactly
// what Word or another tool would write: namespace prefixes, nesting, element order.
internal static class TestDocx
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string StrictWordNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StrictOfficeDocument = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipTypes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string WordContentTypes = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    public static byte[] WithBody(string bodyXml) => WithParts(bodyXml);

    public static byte[] WithParagraphs(params string[] paragraphs) => WithBody(string.Concat(paragraphs.Select(Paragraph)));

    // Each optional part is given as the elements inside its root, such as the
    // <w:style> elements of styles.xml. Hyperlinks are the document's
    // external relationships, by ID.
    public static byte[] WithParts(
        string bodyXml,
        string? styles = null,
        string? numbering = null,
        string? footnotes = null,
        string? endnotes = null,
        IReadOnlyDictionary<string, string>? hyperlinks = null)
    {
        var parts = new List<(string Name, string Content)>();
        var relationships = new List<Relationship>();
        var overrides = new List<(string PartName, string ContentType)>
        {
            ("/word/document.xml", WordContentTypes + "document.main+xml")
        };

        void AddPart(string? content, string name, string root, string relationshipType, string contentType)
        {
            if (content is null)
            {
                return;
            }

            parts.Add(($"word/{name}", $"<w:{root} xmlns:w=\"{WordNamespace}\" xmlns:r=\"{RelationshipsNamespace}\">{content}</w:{root}>"));
            relationships.Add(new Relationship($"rIdPart{relationships.Count}", RelationshipTypes + relationshipType, name));
            overrides.Add(($"/word/{name}", WordContentTypes + contentType));
        }

        AddPart(styles, "styles.xml", "styles", "styles", "styles+xml");
        AddPart(numbering, "numbering.xml", "numbering", "numbering", "numbering+xml");
        AddPart(footnotes, "footnotes.xml", "footnotes", "footnotes", "footnotes+xml");
        AddPart(endnotes, "endnotes.xml", "endnotes", "endnotes", "endnotes+xml");

        foreach (var (id, url) in hyperlinks ?? new Dictionary<string, string>())
        {
            relationships.Add(new Relationship(id, RelationshipTypes + "hyperlink", url, IsExternal: true));
        }

        var documentXml = $"<w:document xmlns:w=\"{WordNamespace}\" xmlns:r=\"{RelationshipsNamespace}\"><w:body>{bodyXml}</w:body></w:document>";
        parts.Add(("word/document.xml", documentXml));
        parts.Add(("word/_rels/document.xml.rels", Relationships([.. relationships])));
        parts.Add(("_rels/.rels", Relationships(new Relationship("rId1", TransitionalOfficeDocument, "word/document.xml"))));
        parts.Add(("[Content_Types].xml", ContentTypes(overrides)));

        return Zip([.. parts]);
    }

    public static byte[] WithDocumentXml(string documentXml) =>
        Zip(
            ("[Content_Types].xml", ContentTypes([("/word/document.xml", WordContentTypes + "document.main+xml")])),
            ("_rels/.rels", Relationships(new Relationship("rId1", TransitionalOfficeDocument, "word/document.xml"))),
            ("word/document.xml", documentXml));

    // As Word writes a document saved as "Strict Open XML Document".
    public static byte[] WithStrictBody(string bodyXml) =>
        Zip(
            ("[Content_Types].xml", ContentTypes([("/word/document.xml", WordContentTypes + "document.main+xml")])),
            ("_rels/.rels", Relationships(new Relationship("rId1", StrictOfficeDocument, "word/document.xml"))),
            ("word/document.xml", $"<w:document xmlns:w=\"{StrictWordNamespace}\"><w:body>{bodyXml}</w:body></w:document>"));

    public static string Paragraph(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    public static string Run(string text, string runProperties = "") =>
        $"<w:r>{(runProperties.Length > 0 ? $"<w:rPr>{runProperties}</w:rPr>" : "")}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    public static string StyledParagraph(string styleId, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{styleId}\"/></w:pPr>{Run(text)}</w:p>";

    public static string ParagraphStyle(string id, string name, string properties = "", string? basedOn = null) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>{(basedOn is null ? "" : $"<w:basedOn w:val=\"{basedOn}\"/>")}{properties}</w:style>";

    public static string CharacterStyle(string id, string name, string runProperties) =>
        $"<w:style w:type=\"character\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:rPr>{runProperties}</w:rPr></w:style>";

    private static string ContentTypes(IEnumerable<(string PartName, string ContentType)> overrides) =>
        $"""
         <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
         <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>{string.Concat(overrides.Select(entry => $"<Override PartName=\"{entry.PartName}\" ContentType=\"{entry.ContentType}\"/>"))}</Types>
         """;
}

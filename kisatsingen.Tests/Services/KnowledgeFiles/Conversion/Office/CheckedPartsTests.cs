using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.TestPackage;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office;

public sealed class CheckedPartsTests
{
    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx), isEditable: false, CheckedParts.OpenSettings);

    private static string Nested(int depth) =>
        string.Concat(Enumerable.Repeat("<w:sdt><w:sdtContent>", depth)) + "<w:p/>" + string.Concat(Enumerable.Repeat("</w:sdtContent></w:sdt>", depth));

    [Fact]
    public void A_part_loads_as_its_root_element()
    {
        using var document = Open(WithBody("<w:p><w:r><w:t>Hei</w:t></w:r></w:p>"));

        var root = new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart);

        Assert.Equal("Hei", root!.Body!.InnerText);
    }

    [Fact]
    public void A_missing_part_loads_as_null()
    {
        using var document = Open(WithBody("<w:p/>"));

        var styles = new CheckedParts(CancellationToken.None).RootOf<Styles>(document.MainDocumentPart!.StyleDefinitionsPart);

        Assert.Null(styles);
    }

    [Fact]
    public void A_part_nested_as_deep_as_real_documents_loads()
    {
        using var document = Open(WithBody(Nested(100)));

        var root = new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart);

        Assert.NotNull(root);
    }

    // Loading takes time quadratic in the depth (20,000 deep took 26 seconds),
    // so this must be refused before the SDK loads it.
    [Fact]
    public void A_part_nested_far_deeper_than_real_documents_is_refused()
    {
        using var document = Open(WithBody(Nested(50_000)));

        Assert.Throws<XmlException>(() => new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart));
    }

    [Fact]
    public void A_part_with_more_elements_than_the_limit_is_refused()
    {
        var millionAndOneRuns = "<w:p>" + string.Concat(Enumerable.Repeat("<w:r/>", 1_000_001)) + "</w:p>";
        using var document = Open(WithBody(millionAndOneRuns));

        Assert.Throws<XmlException>(() => new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart));
    }

    [Fact]
    public void Parts_each_under_the_element_limit_are_refused_when_together_they_are_over_it()
    {
        var body = "<w:p>" + string.Concat(Enumerable.Repeat("<w:r/>", 600_000)) + "</w:p>";
        var styles = string.Concat(Enumerable.Repeat("<w:style/>", 600_000));
        using var document = Open(WithParts(body, styles: styles));
        var parts = new CheckedParts(CancellationToken.None);
        parts.RootOf<Document>(document.MainDocumentPart);

        Assert.Throws<XmlException>(() => parts.RootOf<Styles>(document.MainDocumentPart!.StyleDefinitionsPart));
    }

    // A huge text or attribute is one element, so only the size limit stops it.
    [Fact]
    public void Parts_each_under_the_size_limit_are_refused_when_together_they_are_over_it()
    {
        var text = new string('a', 26_000_000);
        using var document = Open(WithParts(Paragraph(text), styles: $"<w:style><w:name w:val=\"{text}\"/></w:style>"));
        var parts = new CheckedParts(CancellationToken.None);
        parts.RootOf<Document>(document.MainDocumentPart);

        Assert.Throws<XmlException>(() => parts.RootOf<Styles>(document.MainDocumentPart!.StyleDefinitionsPart));
    }

    [Fact]
    public void A_part_is_counted_by_what_it_holds_not_by_the_size_it_declares()
    {
        var docx = WithDeclaredUncompressedSizes(WithStoredEntries(WithParagraphs(new string('a', 51_000_000))), ("word/document.xml", 1_000));
        using var document = Open(docx);

        Assert.Throws<XmlException>(() => new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart));
    }

    // Loading a large part takes a second or more, so a cancelled conversion stops before it.
    [Fact]
    public void Loading_a_part_stops_when_it_is_cancelled()
    {
        using var document = Open(WithBody("<w:p><w:r><w:t>Hei</w:t></w:r></w:p>"));

        Assert.Throws<OperationCanceledException>(() => new CheckedParts(new CancellationToken(canceled: true)).RootOf<Document>(document.MainDocumentPart));
    }

    // The "billion laughs" attack nests entities like these until they expand to gigabytes.
    [Fact]
    public void A_part_with_a_dtd_is_refused()
    {
        var documentWithDtd = $"""<?xml version="1.0"?><!DOCTYPE w:document [<!ENTITY lol "lol">]><w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:r><w:t>&lol;</w:t></w:r></w:p></w:body></w:document>""";
        using var document = Open(WithDocumentXml(documentWithDtd));

        Assert.Throws<XmlException>(() => new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart));
    }

    // Without the setting, the SDK keeps both branches, and a text box's text comes out twice.
    [Fact]
    public void Alternate_content_keeps_one_branch()
    {
        var body = $"""
            <w:p xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><w:r><mc:AlternateContent>
            <mc:Choice Requires="wps"><w:t>Tekst</w:t></mc:Choice><mc:Fallback><w:t>Tekst</w:t></mc:Fallback>
            </mc:AlternateContent></w:r></w:p>
            """;
        using var document = Open(WithBody(body));

        var root = new CheckedParts(CancellationToken.None).RootOf<Document>(document.MainDocumentPart);

        Assert.Equal("Tekst", root!.Body!.InnerText);
    }
}

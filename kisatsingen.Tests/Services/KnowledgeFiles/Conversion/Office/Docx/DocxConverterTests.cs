using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.TestPackage;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxConverterTests
{
    [Fact]
    public void A_document_without_a_main_part_is_unreadable()
    {
        var docx = Zip(("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>"), ("notater.txt", "Ikke Word"));

        var result = Convert(docx);

        Assert.Equal(OfficeConversionResultErrorType.Unreadable, result.ErrorType);
    }

    [Fact]
    public void A_document_without_a_body_is_unreadable()
    {
        var docx = WithDocumentXml($"<w:document xmlns:w=\"{WordNamespace}\"/>");

        var result = Convert(docx);

        Assert.Equal(OfficeConversionResultErrorType.Unreadable, result.ErrorType);
    }

    [Fact]
    public void A_document_nested_too_deep_is_unreadable()
    {
        var nested = string.Concat(Enumerable.Repeat("<w:sdt><w:sdtContent>", 300)) + Paragraph("Dypt") + string.Concat(Enumerable.Repeat("</w:sdtContent></w:sdt>", 300));

        var result = Convert(WithBody(nested));

        Assert.Equal(OfficeConversionResultErrorType.Unreadable, result.ErrorType);
    }

    [Fact]
    public void Markdown_longer_than_the_limit_is_refused_as_too_long()
    {
        var docx = WithParagraphs(new string('a', 101));

        var result = Convert(docx, maxCharacters: 100);

        Assert.Equal(OfficeConversionResultErrorType.TooLong, result.ErrorType);
    }

    // A few bytes of gridSpan ask for billions of cells; the limit stops the writing.
    [Fact]
    public void A_cell_spanning_billions_of_columns_is_refused_as_too_long()
    {
        var docx = WithBody($"<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2000000000\"/></w:tcPr>{Paragraph("Bred")}</w:tc></w:tr></w:tbl>");

        var result = Convert(docx, maxCharacters: 10_000);

        Assert.Equal(OfficeConversionResultErrorType.TooLong, result.ErrorType);
    }

    [Fact]
    public void A_conversion_stops_when_it_is_cancelled()
    {
        var docx = WithParagraphs("Tekst");

        Assert.Throws<OperationCanceledException>(() => Convert(docx, ct: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void A_document_saved_as_strict_open_xml_converts_like_a_transitional_one()
    {
        var docx = WithStrictBody($"<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Streng</w:t></w:r></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("**Streng**", markdown);
    }

    [Fact]
    public void Norwegian_letters_come_through_unchanged()
    {
        var docx = WithParagraphs("Blåbærsyltetøy på Ås, ÆØÅ");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Blåbærsyltetøy på Ås, ÆØÅ", markdown);
    }

    // The counts are for the text-length warning: markers and whitespace aren't text.
    [Fact]
    public void The_written_text_is_counted_without_markers_or_whitespace()
    {
        var docx = WithBody($"<w:p>{Run("Fet tekst", "<w:b/>")}</w:p>");

        var result = Convert(docx);

        Assert.Equal((8, 8), (result.WrittenTextCharacters, result.SourceTextCharacters));
    }

    [Fact]
    public void Text_left_out_on_purpose_is_not_counted_as_the_document_s_text()
    {
        var docx = WithBody($"<w:p>{Run("Synlig")}{Run("Skjult", "<w:vanish/>")}<w:del w:id=\"1\" w:author=\"A\"><w:r><w:delText>Slettet</w:delText></w:r></w:del></w:p>");

        var result = Convert(docx);

        Assert.Equal((6, 6), (result.WrittenTextCharacters, result.SourceTextCharacters));
    }
}

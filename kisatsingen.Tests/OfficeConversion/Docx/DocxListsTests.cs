using System.Xml.Linq;
using kisatsingen.OfficeConversion.Docx;
using Xunit;

namespace kisatsingen.Tests.OfficeConversion.Docx;

public sealed class DocxListsTests
{
    private const string TransitionalNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static DocxLists Lists(params string[] elements) => ListsIn(TransitionalNamespace, elements);

    private static DocxLists ListsIn(string wordNamespace, params string[] elements) =>
        DocxLists.From(XDocument.Parse($"<w:numbering xmlns:w=\"{wordNamespace}\">{string.Concat(elements)}</w:numbering>"));

    private static string AbstractList(string abstractListId, params string[] levels) =>
        $"<w:abstractNum w:abstractNumId=\"{abstractListId}\">{string.Concat(levels)}</w:abstractNum>";

    private static string Level(int level, string format) =>
        $"<w:lvl w:ilvl=\"{level}\"><w:numFmt w:val=\"{format}\"/></w:lvl>";

    private static string ListUsing(string listId, string abstractListId) =>
        $"<w:num w:numId=\"{listId}\"><w:abstractNumId w:val=\"{abstractListId}\"/></w:num>";

    private static DocxListReference InList(string listId, int level = 0) => new(listId, level);

    [Fact]
    public void A_bullet_list_is_bulleted()
    {
        var lists = Lists(AbstractList("0", Level(0, "bullet")), ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList("1")));
    }

    [Theory]
    [InlineData("decimal")]
    [InlineData("lowerLetter")]
    [InlineData("upperRoman")]
    public void A_list_numbered_in_any_format_is_numbered(string format)
    {
        var lists = Lists(AbstractList("0", Level(0, format)), ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList("1")));
    }

    [Fact]
    public void A_level_without_a_format_is_numbered_as_word_does()
    {
        var lists = Lists(AbstractList("0", "<w:lvl w:ilvl=\"0\"/>"), ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList("1")));
    }

    [Fact]
    public void Each_level_of_a_list_has_its_own_format()
    {
        var lists = Lists(AbstractList("0", Level(0, "decimal"), Level(1, "bullet")), ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList("1", level: 1)));
    }

    [Fact]
    public void A_level_that_shows_no_marker_is_no_list_item()
    {
        var lists = Lists(AbstractList("0", Level(0, "none")), ListUsing("1", "0"));

        Assert.Null(lists.KindOf(InList("1")));
    }

    [Fact]
    public void List_zero_is_no_list()
    {
        var lists = Lists(AbstractList("0", Level(0, "bullet")), ListUsing("0", "0"));

        Assert.Null(lists.KindOf(InList("0")));
    }

    [Fact]
    public void A_list_the_document_does_not_define_is_no_list()
    {
        var lists = Lists(AbstractList("0", Level(0, "bullet")), ListUsing("1", "0"));

        Assert.Null(lists.KindOf(InList("7")));
    }

    [Fact]
    public void A_document_without_numbering_has_no_lists()
    {
        var lists = DocxLists.From(null);

        Assert.Null(lists.KindOf(InList("1")));
    }

    // A list linked to a list style defines its levels in the style, so this
    // part doesn't say which; the paragraph is still in a list.
    [Fact]
    public void A_list_whose_levels_are_defined_elsewhere_is_bulleted()
    {
        var lists = Lists("<w:abstractNum w:abstractNumId=\"0\"><w:numStyleLink w:val=\"Punktliste\"/></w:abstractNum>", ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList("1")));
    }

    [Fact]
    public void Numbering_in_a_strict_open_xml_file_is_read()
    {
        var lists = ListsIn(StrictNamespace, AbstractList("0", Level(0, "decimal")), ListUsing("1", "0"));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList("1")));
    }
}

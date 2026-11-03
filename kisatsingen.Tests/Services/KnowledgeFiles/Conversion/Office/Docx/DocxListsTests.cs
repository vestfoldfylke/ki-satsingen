using kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;
using Xunit;
using WordNumbering = DocumentFormat.OpenXml.Wordprocessing.Numbering;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxListsTests
{
    private static DocxLists Lists(params string[] elements) =>
        DocxLists.FromXml(new WordNumbering($"<w:numbering xmlns:w=\"{TestDocx.WordNamespace}\">{string.Concat(elements)}</w:numbering>"));

    private static string AbstractList(int abstractListId, params string[] levels) =>
        $"<w:abstractNum w:abstractNumId=\"{abstractListId}\">{string.Concat(levels)}</w:abstractNum>";

    private static string Level(int level, string format) =>
        $"<w:lvl w:ilvl=\"{level}\"><w:numFmt w:val=\"{format}\"/></w:lvl>";

    private static string ListUsing(int listId, int abstractListId) =>
        $"<w:num w:numId=\"{listId}\"><w:abstractNumId w:val=\"{abstractListId}\"/></w:num>";

    private static DocxListReference InList(int listId, int level = 0) => new(listId, level);

    [Fact]
    public void A_bullet_list_is_bulleted()
    {
        var lists = Lists(AbstractList(0, Level(0, "bullet")), ListUsing(1, 0));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList(1)));
    }

    [Theory]
    [InlineData("decimal")]
    [InlineData("lowerLetter")]
    [InlineData("upperRoman")]
    public void A_list_numbered_in_any_format_is_numbered(string format)
    {
        var lists = Lists(AbstractList(0, Level(0, format)), ListUsing(1, 0));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList(1)));
    }

    // A crafted file can hold a format the SDK can't parse; reading it must not throw.
    [Fact]
    public void A_format_word_does_not_know_is_numbered()
    {
        var lists = Lists(AbstractList(0, Level(0, "sideways")), ListUsing(1, 0));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList(1)));
    }

    [Fact]
    public void A_level_without_a_format_is_numbered_as_word_does()
    {
        var lists = Lists(AbstractList(0, "<w:lvl w:ilvl=\"0\"/>"), ListUsing(1, 0));

        Assert.Equal(DocxListKind.Numbered, lists.KindOf(InList(1)));
    }

    [Fact]
    public void Each_level_of_a_list_has_its_own_format()
    {
        var lists = Lists(AbstractList(0, Level(0, "decimal"), Level(1, "bullet")), ListUsing(1, 0));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList(1, level: 1)));
    }

    [Fact]
    public void A_level_that_shows_no_marker_is_no_list_item()
    {
        var lists = Lists(AbstractList(0, Level(0, "none")), ListUsing(1, 0));

        Assert.Null(lists.KindOf(InList(1)));
    }

    [Fact]
    public void List_zero_is_no_list()
    {
        var lists = Lists(AbstractList(0, Level(0, "bullet")), ListUsing(0, 0));

        Assert.Null(lists.KindOf(InList(0)));
    }

    [Fact]
    public void A_list_the_document_does_not_define_is_no_list()
    {
        var lists = Lists(AbstractList(0, Level(0, "bullet")), ListUsing(1, 0));

        Assert.Null(lists.KindOf(InList(7)));
    }

    [Fact]
    public void A_document_without_numbering_has_no_lists()
    {
        var lists = DocxLists.FromXml(null);

        Assert.Null(lists.KindOf(InList(1)));
    }

    // A list linked to a list style defines its levels in the style, so this
    // part doesn't say which; the paragraph is still in a list.
    [Fact]
    public void A_list_whose_levels_are_defined_elsewhere_is_bulleted()
    {
        var lists = Lists("<w:abstractNum w:abstractNumId=\"0\"><w:numStyleLink w:val=\"Punktliste\"/></w:abstractNum>", ListUsing(1, 0));

        Assert.Equal(DocxListKind.Bulleted, lists.KindOf(InList(1)));
    }
}

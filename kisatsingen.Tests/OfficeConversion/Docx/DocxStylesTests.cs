using System.Xml.Linq;
using kisatsingen.OfficeConversion.Docx;
using Xunit;

namespace kisatsingen.Tests.OfficeConversion.Docx;

public sealed class DocxStylesTests
{
    private const string TransitionalNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    // As Norwegian Word writes them: a localized ID, the built-in English name,
    // and the outline level that makes them headings.
    private static readonly string Overskrift1 = ParagraphStyle("Overskrift1", "heading 1", OutlineLevel(0));
    private static readonly string Overskrift2 = ParagraphStyle("Overskrift2", "heading 2", OutlineLevel(1));

    private static readonly string Punktliste = ParagraphStyle("Punktliste", "List Bullet", ListProperties("5"));

    private static DocxStyles Styles(params string[] styles) => StylesIn(TransitionalNamespace, styles);

    private static DocxStyles StylesIn(string wordNamespace, params string[] styles) =>
        DocxStyles.From(XDocument.Parse($"<w:styles xmlns:w=\"{wordNamespace}\">{string.Concat(styles)}</w:styles>"));

    private static string ParagraphStyle(string id, string name, string properties = "", string? basedOn = null, bool isDefault = false) =>
        Style("paragraph", id, name, properties, basedOn, isDefault);

    private static string CharacterStyle(string id, string name, string properties = "", string? basedOn = null) =>
        Style("character", id, name, properties, basedOn, isDefault: false);

    private static string Style(string type, string id, string name, string properties, string? basedOn, bool isDefault) =>
        $"<w:style w:type=\"{type}\" w:styleId=\"{id}\"{(isDefault ? " w:default=\"1\"" : "")}>" +
        $"<w:name w:val=\"{name}\"/>{(basedOn is null ? "" : $"<w:basedOn w:val=\"{basedOn}\"/>")}{properties}</w:style>";

    private static string OutlineLevel(int level) => $"<w:pPr><w:outlineLvl w:val=\"{level}\"/></w:pPr>";

    private static string ListProperties(string listId, int level = 0) =>
        $"<w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{listId}\"/></w:numPr></w:pPr>";

    private static string RunProperties(string properties) => $"<w:rPr>{properties}</w:rPr>";

    [Fact]
    public void A_norwegian_heading_style_is_a_heading_by_its_outline_level()
    {
        var styles = Styles(Overskrift1);

        Assert.Equal(0, styles.OutlineLevelOf("Overskrift1"));
    }

    [Fact]
    public void A_style_based_on_a_heading_style_is_a_heading_of_the_same_level()
    {
        var styles = Styles(Overskrift2, ParagraphStyle("MinOverskrift", "Min overskrift", basedOn: "Overskrift2"));

        Assert.Equal(1, styles.OutlineLevelOf("MinOverskrift"));
    }

    [Fact]
    public void A_style_s_own_outline_level_wins_over_the_one_it_is_based_on()
    {
        var styles = Styles(Overskrift1, ParagraphStyle("Underoverskrift", "Underoverskrift", OutlineLevel(2), basedOn: "Overskrift1"));

        Assert.Equal(2, styles.OutlineLevelOf("Underoverskrift"));
    }

    // Outline level 9 is Word's "body text": it cancels the heading level
    // the style would otherwise inherit.
    [Fact]
    public void A_style_marked_as_body_text_is_no_heading_even_when_based_on_one()
    {
        var styles = Styles(Overskrift1, ParagraphStyle("Brødtekst", "Brødtekst", OutlineLevel(9), basedOn: "Overskrift1"));

        Assert.Null(styles.OutlineLevelOf("Brødtekst"));
    }

    [Theory]
    [InlineData("Tittel", "Title", 0)]
    [InlineData("Undertittel", "Subtitle", 1)]
    public void Title_and_subtitle_are_the_top_two_heading_levels_by_their_built_in_name(string id, string name, int expectedOutlineLevel)
    {
        var styles = Styles(ParagraphStyle(id, name));

        Assert.Equal(expectedOutlineLevel, styles.OutlineLevelOf(id));
    }

    // Some tools write a heading style's built-in name but no outline level.
    [Theory]
    [InlineData("Heading1", "heading 1", 0)]
    [InlineData("Overskrift3", "heading 3", 2)]
    [InlineData("Heading9", "Heading 9", 8)]
    public void A_heading_style_without_an_outline_level_is_a_heading_by_its_built_in_name(string id, string name, int expectedOutlineLevel)
    {
        var styles = Styles(ParagraphStyle(id, name));

        Assert.Equal(expectedOutlineLevel, styles.OutlineLevelOf(id));
    }

    [Fact]
    public void A_style_without_an_outline_level_is_no_heading()
    {
        var styles = Styles(ParagraphStyle("Normal", "Normal"));

        Assert.Null(styles.OutlineLevelOf("Normal"));
    }

    [Fact]
    public void A_style_the_document_does_not_define_is_no_heading()
    {
        var styles = Styles(Overskrift1);

        Assert.Null(styles.OutlineLevelOf("Finnesikke"));
    }

    [Fact]
    public void A_document_without_styles_has_no_headings()
    {
        var styles = DocxStyles.From(null);

        Assert.Null(styles.OutlineLevelOf("Overskrift1"));
    }

    // Word never writes a loop, but a crafted file can; it must not hang.
    [Theory]
    [InlineData("A", "A")]
    [InlineData("A", "B")]
    public void A_chain_of_styles_that_loops_back_ends_without_a_heading(string firstBasedOn, string secondBasedOn)
    {
        var styles = Styles(ParagraphStyle("A", "A", basedOn: firstBasedOn), ParagraphStyle("B", "B", basedOn: secondBasedOn));

        Assert.Null(styles.OutlineLevelOf("A"));
    }

    [Fact]
    public void Styles_in_a_strict_open_xml_file_are_read()
    {
        var styles = StylesIn(StrictNamespace, Overskrift1);

        Assert.Equal(0, styles.OutlineLevelOf("Overskrift1"));
    }

    [Fact]
    public void A_paragraph_without_a_style_has_the_default_paragraph_style()
    {
        var styles = Styles(ParagraphStyle("Normal", "Normal", RunProperties("<w:i/>"), isDefault: true));

        Assert.True(styles.FormattingOf(paragraphStyleId: null, characterStyleId: null).IsItalic);
    }

    [Theory]
    [InlineData("<w:b/>", true, false, false)]
    [InlineData("<w:i/>", false, true, false)]
    [InlineData("<w:strike/>", false, false, true)]
    [InlineData("<w:dstrike/>", false, false, true)]
    [InlineData("<w:b/><w:i/>", true, true, false)]
    public void A_character_style_makes_its_text_bold_italic_or_struck(string properties, bool isBold, bool isItalic, bool isStruck)
    {
        var styles = Styles(CharacterStyle("Sterk", "Strong", RunProperties(properties)));

        Assert.Equal(new RunFormatting(isBold, isItalic, isStruck), styles.FormattingOf(paragraphStyleId: null, characterStyleId: "Sterk"));
    }

    [Fact]
    public void A_paragraph_style_s_formatting_applies_to_runs_without_a_character_style()
    {
        var styles = Styles(ParagraphStyle("Viktig", "Viktig", RunProperties("<w:b/>")));

        Assert.True(styles.FormattingOf("Viktig", characterStyleId: null).IsBold);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public void A_character_style_can_switch_off_formatting_its_paragraph_style_switches_on(string switchedOff)
    {
        var styles = Styles(
            ParagraphStyle("Viktig", "Viktig", RunProperties("<w:b/>")),
            CharacterStyle("Vanlig", "Vanlig", RunProperties($"<w:b w:val=\"{switchedOff}\"/>")));

        Assert.False(styles.FormattingOf("Viktig", "Vanlig").IsBold);
    }

    [Fact]
    public void A_character_style_inherits_formatting_from_the_style_it_is_based_on()
    {
        var styles = Styles(
            CharacterStyle("Sterk", "Strong", RunProperties("<w:b/>")),
            CharacterStyle("SterkRød", "Sterk rød", basedOn: "Sterk"));

        Assert.True(styles.FormattingOf(paragraphStyleId: null, "SterkRød").IsBold);
    }

    [Fact]
    public void A_list_style_puts_its_paragraphs_in_its_list()
    {
        var styles = Styles(Punktliste);

        Assert.Equal(new DocxListReference("5", Level: 0), styles.ListOf("Punktliste"));
    }

    [Fact]
    public void A_list_style_s_level_is_read()
    {
        var styles = Styles(ParagraphStyle("Punktliste2", "List Bullet 2", ListProperties("5", level: 1)));

        Assert.Equal(new DocxListReference("5", Level: 1), styles.ListOf("Punktliste2"));
    }

    [Fact]
    public void A_style_based_on_a_list_style_is_in_the_same_list()
    {
        var styles = Styles(Punktliste, ParagraphStyle("MinListe", "Min liste", basedOn: "Punktliste"));

        Assert.Equal(new DocxListReference("5", Level: 0), styles.ListOf("MinListe"));
    }

    // numId 0 is how a style says "no list"; it must not fall through to the base style's list.
    [Fact]
    public void A_style_that_cancels_its_base_style_s_list_says_list_zero()
    {
        var styles = Styles(Punktliste, ParagraphStyle("UtenListe", "Uten liste", ListProperties("0"), basedOn: "Punktliste"));

        Assert.Equal(new DocxListReference("0", Level: 0), styles.ListOf("UtenListe"));
    }
}

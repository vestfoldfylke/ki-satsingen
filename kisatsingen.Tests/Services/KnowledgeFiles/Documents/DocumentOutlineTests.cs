using kisatsingen.Services.KnowledgeFiles.Documents;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Documents;

public sealed class DocumentOutlineTests
{
    private static IReadOnlyList<OutlineEntry> Headings(string markdown) =>
        DocumentOutline.Build(markdown).Where(entry => entry.Kind == OutlineKind.Heading).ToList();

    [Fact]
    public void A_section_runs_until_the_next_heading_at_its_own_level_or_above()
    {
        var headings = Headings("# A\ntekst\n## A.1\ntekst\n# B\ntekst");

        Assert.Equal(
            [new OutlineEntry(1, 4, OutlineKind.Heading, 1, "A"), new OutlineEntry(3, 4, OutlineKind.Heading, 2, "A.1"), new OutlineEntry(5, 6, OutlineKind.Heading, 1, "B")],
            headings);
    }

    // Otherwise a model navigating by outline would never see the introduction.
    [Fact]
    public void Text_before_the_first_heading_gets_an_entry_of_its_own()
    {
        var first = DocumentOutline.Build("Innledende tekst.\n\nMer.\n# Kapittel\ntekst")[0];

        Assert.Equal(new OutlineEntry(1, 3, OutlineKind.Block, 0, "Innledende tekst."), first);
    }

    [Fact]
    public void Blank_lines_before_the_first_heading_get_no_entry()
    {
        var first = DocumentOutline.Build("\n\n# Kapittel\ntekst")[0];

        Assert.Equal(OutlineKind.Heading, first.Kind);
    }

    [Fact]
    public void Underlined_headings_count_as_headings()
    {
        var heading = Assert.Single(Headings("Innledning\n==========\n\ntekst"));

        Assert.Equal((1, 1, "Innledning"), (heading.Line, heading.Level, heading.Title));
    }

    [Fact]
    public void A_hash_inside_a_code_block_is_not_a_heading()
    {
        var headings = Headings("# Ekte\n\n```bash\n# kommentar\n```");

        Assert.Equal(["Ekte"], headings.Select(heading => heading.Title));
    }

    [Fact]
    public void A_heading_title_is_its_text_without_emphasis_markers()
    {
        var heading = Assert.Single(Headings("# **Viktig** om `kode`"));

        Assert.Equal("Viktig om kode", heading.Title);
    }

    [Fact]
    public void A_table_is_listed_with_the_lines_it_covers()
    {
        var table = Assert.Single(DocumentOutline.Build("# Tall\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\netter"), entry => entry.Kind == OutlineKind.Table);

        Assert.Equal((3, 5), (table.Line, table.EndLine));
    }

    // Typical for .txt: skimmable blocks instead of an empty outline.
    [Fact]
    public void A_document_without_headings_is_outlined_in_blocks_that_cover_every_line()
    {
        var lines = Enumerable.Range(1, 250).Select(number => number % 20 == 0 ? string.Empty : $"linje {number}");

        var blocks = DocumentOutline.Build(string.Join('\n', lines));

        Assert.Equal(1, blocks[0].Line);
        Assert.Equal(250, blocks[^1].EndLine);
        Assert.All(blocks.Zip(blocks.Skip(1)), pair => Assert.Equal(pair.First.EndLine + 1, pair.Second.Line));
    }

    [Fact]
    public void A_fallback_block_ends_at_a_blank_line_rather_than_mid_paragraph()
    {
        var lines = Enumerable.Range(1, 150).Select(number => number == 105 ? string.Empty : $"linje {number}");

        var first = DocumentOutline.Build(string.Join('\n', lines))[0];

        Assert.Equal(105, first.EndLine);
    }
}

using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Services.KnowledgeFiles.Documents;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.FileTools;

public sealed class OutlineSelectionTests
{
    private const string Document = "# A\n## A1\n### A1a\ntekst\n## A2\n# B\n## B1";
    private const int NoEntryCap = 1_000;

    private static OutlineSelectionResult Select(string markdown, int start, int end, int depth, int maxEntries = NoEntryCap) =>
        OutlineSelection.Select(DocumentOutline.Build(markdown), start, end, depth, maxEntries);

    private static IEnumerable<string> Titles(OutlineSelectionResult selection) => selection.Entries.Select(entry => entry.Title);

    [Fact]
    public void Depth_2_shows_the_two_highest_heading_levels()
    {
        var selection = Select(Document, start: 1, end: 7, depth: 2);

        Assert.Equal(["A", "A1", "A2", "B", "B1"], Titles(selection));
    }

    [Fact]
    public void Depth_counts_from_the_highest_level_in_the_file_even_when_the_range_is_one_section()
    {
        var selection = Select(Document, start: 2, end: 5, depth: 2);

        Assert.Equal(["A1", "A2"], Titles(selection));
    }

    [Fact]
    public void A_section_shows_its_subsections_at_a_higher_depth()
    {
        var selection = Select(Document, start: 2, end: 5, depth: 3);

        Assert.Equal(["A1", "A1a", "A2"], Titles(selection));
    }

    [Fact]
    public void A_continuation_past_the_top_heading_shows_the_same_levels_as_the_page_before_it()
    {
        const string titledDocument = "# T\n## S1\n### s1\n## S2\n### s2\n## S3";
        var firstPage = Select(titledDocument, start: 1, end: 6, depth: 2, maxEntries: 2);

        var secondPage = Select(titledDocument, start: firstPage.ContinueFromLine!.Value, end: 6, depth: 2, maxEntries: 2);

        Assert.Equal(["S2", "S3"], Titles(secondPage));
    }

    [Fact]
    public void A_document_whose_top_headings_are_level_2_still_shows_two_levels()
    {
        var selection = Select("## X\n### Y\n#### Z", start: 1, end: 3, depth: 2);

        Assert.Equal(["X", "Y"], Titles(selection));
    }

    [Fact]
    public void Only_entries_that_start_inside_the_range_are_shown()
    {
        var selection = Select(Document, start: 6, end: 7, depth: 2);

        Assert.Equal(["B", "B1"], Titles(selection));
    }

    [Fact]
    public void Tables_are_shown_whatever_the_depth()
    {
        var selection = Select("# A\n## A1\n\n| a | b |\n|---|---|\n| 1 | 2 |", start: 1, end: 6, depth: 1);

        Assert.Equal([OutlineKind.Heading, OutlineKind.Table], selection.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public void A_file_without_headings_is_outlined_as_blocks()
    {
        var selection = Select("Første linje\nandre linje", start: 1, end: 2, depth: 2);

        Assert.Equal([OutlineKind.Block], selection.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public void An_outline_over_the_entry_cap_is_cut_and_says_which_line_to_continue_from()
    {
        var selection = Select(Document, start: 1, end: 7, depth: 2, maxEntries: 2);

        Assert.Equal(["A", "A1"], Titles(selection));
        Assert.Equal(5, selection.ContinueFromLine);
    }
}

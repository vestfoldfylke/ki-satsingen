using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

public sealed class DocxTextCountTests
{
    [Fact]
    public void Text_up_to_the_output_limit_is_read()
    {
        var tally = new DocxTextTally(maxCharacters: 5);

        tally.Add("abcde");

        Assert.Equal(5, tally.Characters);
    }

    // Stopped while reading, before a huge paragraph is copied into strings.
    [Fact]
    public void Text_past_the_output_limit_stops_the_conversion_as_too_long()
    {
        var tally = new DocxTextTally(maxCharacters: 5);
        tally.Add("abc");

        Assert.Throws<MarkdownTooLongException>(() => tally.Add("def"));
    }

    [Fact]
    public void Whitespace_does_not_count_toward_the_output_limit()
    {
        var tally = new DocxTextTally(maxCharacters: 3);

        tally.Add("  a b\tc  ");

        Assert.Equal(3, tally.Characters);
    }
}

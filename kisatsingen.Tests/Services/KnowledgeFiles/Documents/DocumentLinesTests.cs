using kisatsingen.Services.KnowledgeFiles.Documents;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Documents;

public sealed class DocumentLinesTests
{
    [Fact]
    public void A_final_newline_ends_the_last_line_rather_than_starting_an_empty_one()
    {
        Assert.Equal(["en", "to"], DocumentLines.Split("en\nto\n"));
    }

    [Fact]
    public void Blank_lines_inside_the_text_are_lines()
    {
        Assert.Equal(["en", "", "tre"], DocumentLines.Split("en\n\ntre"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("en")]
    [InlineData("en\n")]
    [InlineData("en\n\n")]
    [InlineData("en\n\ntre")]
    public void Count_agrees_with_the_number_of_lines_split_returns(string markdown)
    {
        Assert.Equal(DocumentLines.Split(markdown).Length, DocumentLines.Count(markdown));
    }
}

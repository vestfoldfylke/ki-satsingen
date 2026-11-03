using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office;

public sealed class MarkdownOutputTests
{
    [Fact]
    public void Output_up_to_the_limit_is_written()
    {
        var output = new MarkdownOutput(maxCharacters: 5);

        output.Append("12345");

        Assert.Equal("12345", output.ToString());
    }

    [Fact]
    public void Output_one_character_past_the_limit_is_refused()
    {
        var output = new MarkdownOutput(maxCharacters: 5);
        output.Append("1234");

        Assert.Throws<MarkdownTooLongException>(() => output.Append("56"));
    }
}

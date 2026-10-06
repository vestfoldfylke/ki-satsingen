using kisatsingen.AIFunctions.FileTools;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.FileTools;

public sealed class FileToolOptionsTests
{
    [Fact]
    public void The_defaults_are_valid()
    {
        var exception = Record.Exception(() => new FileToolOptions().Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0, 2_000, 2, 200)]
    [InlineData(8_000, 0, 2, 200)]
    [InlineData(8_000, 2_000, 0, 200)]
    [InlineData(8_000, 2_000, 2, 0)]
    public void A_setting_that_is_not_positive_is_refused(int maxReadTokens, int maxLineCharacters, int defaultOutlineDepth, int maxOutlineEntries)
    {
        var options = new FileToolOptions
        {
            MaxReadTokens = maxReadTokens,
            MaxLineCharacters = maxLineCharacters,
            DefaultOutlineDepth = defaultOutlineDepth,
            MaxOutlineEntries = maxOutlineEntries
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void A_default_depth_deeper_than_markdowns_heading_levels_is_refused()
    {
        var options = new FileToolOptions { DefaultOutlineDepth = GetOutlineTool.MaxDepth + 1 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void A_line_cap_that_could_fill_a_whole_read_is_refused()
    {
        var options = new FileToolOptions { MaxReadTokens = 100, MaxLineCharacters = 2_000 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}

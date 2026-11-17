using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationFileNameTests
{
    [Theory]
    [InlineData("Budsjett 2027", "Budsjett 2027.pptx")]
    [InlineData("Plan: skole/fag?", "Plan skole fag.pptx")]
    [InlineData("  Mange   mellomrom  ", "Mange mellomrom.pptx")]
    [InlineData("...", "presentasjon.pptx")]
    [InlineData("/:*", "presentasjon.pptx")]
    public void The_file_name_is_the_title_without_characters_a_file_name_cannot_hold(string title, string expected)
    {
        Assert.Equal(expected, PresentationFileName.From(title));
    }

    [Fact]
    public void A_long_title_is_cut_to_the_maximum_length()
    {
        var fileName = PresentationFileName.From(new string('a', PresentationFileName.MaxStemLength + 50));

        Assert.Equal(PresentationFileName.MaxStemLength + PresentationFileName.Extension.Length, fileName.Length);
    }

    [Fact]
    public void A_cut_never_splits_an_emoji()
    {
        var fileName = PresentationFileName.From(new string('a', PresentationFileName.MaxStemLength - 1) + "📈");

        Assert.Equal(new string('a', PresentationFileName.MaxStemLength - 1) + PresentationFileName.Extension, fileName);
    }
}

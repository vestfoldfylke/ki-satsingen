using kisatsingen.Services.GeneratedFiles;
using Xunit;

namespace kisatsingen.Tests.Services.GeneratedFiles;

public sealed class DownloadFileNameTests
{
    private const string Extension = ".pptx";
    private const string FallbackStem = "presentasjon";

    private static string From(string title) => DownloadFileName.From(title, Extension, FallbackStem);

    [Theory]
    [InlineData("Budsjett 2027", "Budsjett 2027.pptx")]
    [InlineData("Plan: skole/fag?", "Plan skole fag.pptx")]
    [InlineData("  Mange   mellomrom  ", "Mange mellomrom.pptx")]
    [InlineData("...", "presentasjon.pptx")]
    [InlineData("/:*", "presentasjon.pptx")]
    public void The_file_name_is_the_title_without_characters_a_file_name_cannot_hold(string title, string expected)
    {
        Assert.Equal(expected, From(title));
    }

    [Fact]
    public void The_file_name_takes_the_extension_and_fallback_it_is_given()
    {
        Assert.Equal(("Notat.docx", "dokument.docx"), (DownloadFileName.From("Notat", ".docx", "dokument"), DownloadFileName.From("...", ".docx", "dokument")));
    }

    [Fact]
    public void A_long_title_is_cut_to_the_maximum_length()
    {
        var fileName = From(new string('a', DownloadFileName.MaxStemLength + 50));

        Assert.Equal(DownloadFileName.MaxStemLength + Extension.Length, fileName.Length);
    }

    [Fact]
    public void A_cut_never_splits_an_emoji()
    {
        var fileName = From(new string('a', DownloadFileName.MaxStemLength - 1) + "📈");

        Assert.Equal(new string('a', DownloadFileName.MaxStemLength - 1) + Extension, fileName);
    }
}

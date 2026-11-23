using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class TextFitTests
{
    // The template's quote box: 23.2 × 11 cm at 50 pt.
    private static readonly TextBox QuoteBox = new(WidthPoints: 657, HeightPoints: 312, FontSizePoints: 50);

    [Fact]
    public void A_short_text_keeps_the_templates_size()
    {
        var size = TextFit.FontSizeToFit(["Vi må prioritere skole."], QuoteBox);

        Assert.Null(size);
    }

    [Fact]
    public void A_text_too_long_for_its_box_gets_a_smaller_size_at_which_it_fits()
    {
        var quote = string.Join(' ', Enumerable.Repeat("Fylkeskommunen skal levere gode tjenester til alle innbyggere.", 4));

        var size = TextFit.FontSizeToFit([quote], QuoteBox);

        Assert.InRange(size!.Value, 2_000, 4_900);
        Assert.Null(TextFit.FontSizeToFit([quote], QuoteBox with { FontSizePoints = size.Value / 100.0 }));
    }

    [Fact]
    public void Text_that_cannot_fit_is_shrunk_no_further_than_two_fifths_of_the_templates_size()
    {
        var size = TextFit.FontSizeToFit([new string('x', 10_000)], QuoteBox);

        Assert.Equal(2_000, size);
    }

    [Fact]
    public void Text_is_never_shrunk_below_ten_points()
    {
        var size = TextFit.FontSizeToFit([new string('x', 10_000)], new TextBox(WidthPoints: 400, HeightPoints: 50, FontSizePoints: 14));

        Assert.Equal(1_000, size);
    }

    [Fact]
    public void Every_paragraph_starts_a_new_line()
    {
        var size = TextFit.FontSizeToFit(["A", "B", "C"], new TextBox(WidthPoints: 400, HeightPoints: 40, FontSizePoints: 20));

        Assert.NotNull(size);
    }

    [Theory]
    [InlineData("to ord", 10, 1)]
    [InlineData("ordene brytes ved mellomrom", 10, 3)]
    [InlineData("Fylkeskommunen", 5, 3)]
    [InlineData("", 10, 1)]
    public void Words_wrap_at_spaces_and_a_word_longer_than_a_line_is_broken(string paragraph, int charactersPerLine, int expectedLines)
    {
        var lines = TextFit.CountLines(paragraph, charactersPerLine);

        Assert.Equal(expectedLines, lines);
    }
}

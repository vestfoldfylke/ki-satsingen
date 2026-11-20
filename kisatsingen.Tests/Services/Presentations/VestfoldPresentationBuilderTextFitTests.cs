using DocumentFormat.OpenXml.Packaging;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Tests.Services.Presentations;

// Against the real template, so a template whose boxes or sizes change in a
// way PlaceholderTextBoxes cannot read fails here.
public sealed class VestfoldPresentationBuilderTextFitTests
{
    [Fact]
    public void A_short_quote_keeps_the_templates_font_size()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.Quote, "Vi må prioritere skole.", Text: "Fylkesordføreren"));

        Assert.All(slide.Slide!.Descendants<A.RunProperties>(), run => Assert.Null(run.FontSize));
    }

    [Fact]
    public void A_quote_too_long_for_the_templates_size_is_shrunk_below_it()
    {
        var quote = string.Join(' ', Enumerable.Repeat("Fylkeskommunen skal levere gode tjenester til alle innbyggere.", 4));

        var slide = BuildSingle(new SlideSpec(SlideKind.Quote, quote));

        var size = slide.Slide!.Descendants<A.Run>().Single(run => run.Text?.Text == quote).RunProperties?.FontSize?.Value;
        Assert.InRange(size ?? int.MaxValue, 2_000, 4_900);
    }

    [Fact]
    public void A_big_number_that_fills_the_circle_is_shrunk_to_stay_inside_it()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.BigNumber, "Antall elever", Text: "12 345 678"));

        var size = slide.Slide!.Descendants<A.Run>().Single(run => run.Text?.Text == "12 345 678").RunProperties?.FontSize?.Value;
        Assert.InRange(size ?? int.MaxValue, 1, 24_900);
    }

    // The figure that wrapped onto two lines in PowerPoint: the circle's text
    // area is the square inside it, not the whole circle.
    [Fact]
    public void A_three_digit_big_number_is_shrunk_to_one_line_inside_the_circles_text_area()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.BigNumber, "Tallet 250", Text: "250"));

        var size = slide.Slide!.Descendants<A.Run>().Single(run => run.Text?.Text == "250").RunProperties?.FontSize?.Value;
        Assert.InRange(size ?? int.MaxValue, 10_000, 20_000);
    }

    [Fact]
    public void A_two_character_big_number_keeps_the_templates_size()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.BigNumber, "Tallet 7", Text: "7"));

        Assert.Null(slide.Slide!.Descendants<A.Run>().Single(run => run.Text?.Text == "7").RunProperties?.FontSize);
    }

    private static SlidePart BuildSingle(SlideSpec slide)
    {
        var bytes = new VestfoldPresentationBuilder(new PresentationTemplate(), PictureCatalog.Embedded, TimeProvider.System)
            .Build(new PresentationSpec("Tekst", PresentationTheme.SivStrand, [slide]))
            .ToArray();
        var document = PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);
        return Assert.Single(document.PresentationPart!.SlideParts);
    }
}

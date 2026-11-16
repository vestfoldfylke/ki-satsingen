using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class VestfoldPresentationBuilderTests
{
    private static readonly PresentationSpec Deck = new("Budsjett 2027", PresentationTheme.VannFjord,
    [
        new(SlideKind.Cover, "Budsjett 2027", Text: "Forslag til fylkestinget", Bullets: ["Økonomiavdelingen", "12. november 2026"]),
        new(SlideKind.Agenda, "Agenda", Bullets: ["Rammer", "Prioriteringer"]),
        new(SlideKind.Chapter, "Rammer"),
        new(SlideKind.Content, "Hovedtall", Bullets: ["Driftsramme øker", "Investeringer flates ut"]),
        new(SlideKind.TwoColumns, "Før og etter", Bullets: ["Før"], SecondColumn: ["Etter"]),
        new(SlideKind.Quote, "Vi må prioritere skole.", Text: "Fylkesordføreren"),
        new(SlideKind.BigNumber, "Andel til skole", Text: "41 %"),
        new(SlideKind.Closing, "Takk for meg!", Text: "post@vestfoldfylke.no")
    ]);

    public static TheoryData<SlideKind, PresentationTheme> EveryKindInEveryTheme()
    {
        var data = new TheoryData<SlideKind, PresentationTheme>();
        foreach (var kind in Enum.GetValues<SlideKind>())
        {
            foreach (var theme in Enum.GetValues<PresentationTheme>())
            {
                data.Add(kind, theme);
            }
        }
        return data;
    }

    [Fact]
    public void The_built_file_is_a_presentation_rather_than_a_template()
    {
        using var document = Open(Build(Deck));

        Assert.Equal(PresentationDocumentType.Presentation, document.DocumentType);
    }

    [Fact]
    public void The_slides_the_file_writes_have_no_schema_errors()
    {
        using var document = Open(Build(Deck));
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);

        var errors = new OpenXmlPart[] { document.PresentationPart! }
            .Concat(document.PresentationPart!.SlideParts)
            .SelectMany(validator.Validate)
            .Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");

        Assert.Empty(errors);
    }

    [Fact]
    public void Only_the_built_slides_remain_in_the_file()
    {
        using var document = Open(Build(Deck));

        Assert.Equal(
            (Deck.Slides.Count, Deck.Slides.Count),
            (document.PresentationPart!.Presentation!.SlideIdList!.Elements<SlideId>().Count(), document.PresentationPart.SlideParts.Count()));
    }

    [Fact]
    public void The_template_guide_slides_are_deleted_from_the_package_not_only_unlisted()
    {
        using var zip = new ZipArchive(new MemoryStream(Build(Deck)), ZipArchiveMode.Read);

        var slideEntries = zip.Entries.Count(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal));

        Assert.Equal(Deck.Slides.Count, slideEntries);
    }

    [Fact]
    public void The_template_sections_are_removed_so_none_point_at_deleted_slides()
    {
        using var document = Open(Build(Deck));

        Assert.DoesNotContain("sectionLst", document.PresentationPart!.Presentation!.OuterXml);
    }

    [Fact]
    public void Each_slide_uses_its_kinds_layout_in_the_chosen_themes_master()
    {
        using var document = Open(Build(Deck));
        var expectedMaster = MasterAt(document, (int)Deck.Theme);

        var layouts = OrderedSlides(document).Select(slide => (slide.SlideLayoutPart!.SlideLayout!.CommonSlideData!.Name!.Value!.Trim(), slide.SlideLayoutPart.SlideMasterPart == expectedMaster));

        Assert.Equal(Deck.Slides.Select(slide => (LayoutNameOf(slide.Kind), true)), layouts);
    }

    [Fact]
    public void Each_slide_holds_the_text_it_was_given_in_order()
    {
        using var document = Open(Build(Deck));

        var cover = OrderedSlides(document).First();
        var texts = cover.Slide!.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(text => text.Text);

        Assert.Equal(["Budsjett 2027", "Forslag til fylkestinget", "Økonomiavdelingen", "12. november 2026"], texts);
    }

    [Fact]
    public void Every_placeholder_on_a_slide_refers_to_a_placeholder_in_its_layout()
    {
        using var document = Open(Build(Deck));

        foreach (var slide in OrderedSlides(document))
        {
            var layoutIndexes = slide.SlideLayoutPart!.SlideLayout!.Descendants<PlaceholderShape>().Select(Key).ToHashSet();
            Assert.All(slide.Slide!.Descendants<PlaceholderShape>().Select(Key), key => Assert.Contains(key, layoutIndexes));
        }
    }

    [Fact]
    public void The_presentation_title_becomes_the_document_title()
    {
        using var document = Open(Build(Deck));

        Assert.Equal("Budsjett 2027", document.PackageProperties.Title);
    }

    [Theory]
    [MemberData(nameof(EveryKindInEveryTheme))]
    public void Every_slide_kind_builds_in_every_theme(SlideKind kind, PresentationTheme theme)
    {
        var slide = Deck.Slides.First(s => s.Kind == kind);

        using var document = Open(Build(Deck with { Theme = theme, Slides = [slide] }));

        Assert.Equal(MasterAt(document, (int)theme), Assert.Single(document.PresentationPart!.SlideParts).SlideLayoutPart!.SlideMasterPart);
    }

    // By the colour theme's own name in the template, not by master order, so a
    // template whose masters are reordered fails here instead of shipping the
    // wrong colours.
    [Theory]
    [InlineData(PresentationTheme.SivStrand, "Fargetema Siv/Strand")]
    [InlineData(PresentationTheme.VannFjord, "Fargetema Vann/Fjord")]
    [InlineData(PresentationTheme.BarkNype, "Fargetema Bark/Nype")]
    [InlineData(PresentationTheme.GranKorn, "Fargetema Gran/Korn")]
    public void Each_theme_gives_the_slides_the_template_colour_theme_of_the_same_name(PresentationTheme theme, string themeName)
    {
        using var document = Open(Build(Deck with { Theme = theme }));

        Assert.All(
            document.PresentationPart!.SlideParts,
            slide => Assert.Equal(themeName, slide.SlideLayoutPart!.SlideMasterPart!.ThemePart!.Theme!.Name?.Value));
    }

    private static byte[] Build(PresentationSpec spec) => new VestfoldPresentationBuilder().Build(spec);

    private static PresentationDocument Open(byte[] bytes) => PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);

    private static IEnumerable<SlidePart> OrderedSlides(PresentationDocument document) =>
        document.PresentationPart!.Presentation!.SlideIdList!.Elements<SlideId>()
            .Select(id => (SlidePart)document.PresentationPart.GetPartById(id.RelationshipId!.Value!));

    private static SlideMasterPart MasterAt(PresentationDocument document, int index)
    {
        var masterId = document.PresentationPart!.Presentation!.SlideMasterIdList!.Elements<SlideMasterId>().ElementAt(index);
        return (SlideMasterPart)document.PresentationPart.GetPartById(masterId.RelationshipId!.Value!);
    }

    private static string LayoutNameOf(SlideKind kind) => kind switch
    {
        SlideKind.Cover => "Forside - Kun tekst 1",
        SlideKind.Agenda => "Agenda",
        SlideKind.Chapter => "Kapittelinndeler - Tekst",
        SlideKind.Content => "Tittel og innhold 1 kolonne",
        SlideKind.TwoColumns => "Tittel og innhold 2 kolonner",
        SlideKind.Quote => "Stort sitat",
        SlideKind.BigNumber => "Tall i sirkel",
        SlideKind.Closing => "Avsluttende lysbilde",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static (string?, uint?) Key(PlaceholderShape placeholder) =>
        (placeholder.Type?.InnerText, placeholder.Index?.Value);
}

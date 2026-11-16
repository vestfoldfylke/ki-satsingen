using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationSpecValidatorTests
{
    private static readonly SlideSpec Content = new(SlideKind.Content, "Hovedtall", Bullets: ["Ett punkt"]);

    private static PresentationSpec Spec(params SlideSpec[] slides) => new("Tittel", PresentationTheme.SivStrand, slides);

    [Fact]
    public void A_spec_using_every_kinds_own_fields_has_no_problems()
    {
        var spec = Spec(
            new(SlideKind.Cover, "Forside", Text: "Undertittel", Bullets: ["Navn", "Dato"]),
            new(SlideKind.Agenda, "Agenda", Bullets: ["Tema"]),
            new(SlideKind.Chapter, "Kapittel"),
            Content,
            new(SlideKind.TwoColumns, "To", Bullets: ["Venstre"], SecondColumn: ["Høyre"]),
            new(SlideKind.Quote, "Sitat", Text: "Noen"),
            new(SlideKind.BigNumber, "Andel", Text: "41 %"),
            new(SlideKind.Closing, "Takk", Text: "post@vestfoldfylke.no"));

        Assert.Empty(PresentationSpecValidator.Validate(spec));
    }

    [Fact]
    public void A_presentation_without_slides_is_refused()
    {
        Assert.Single(PresentationSpecValidator.Validate(Spec()));
    }

    [Fact]
    public void A_presentation_with_more_slides_than_the_limit_is_refused()
    {
        var slides = Enumerable.Repeat(Content, PresentationSpecValidator.MaxSlides + 1).ToArray();

        Assert.Single(PresentationSpecValidator.Validate(Spec(slides)));
    }

    [Fact]
    public void A_slide_without_a_title_is_refused_naming_the_slide()
    {
        var problem = Assert.Single(PresentationSpecValidator.Validate(Spec(Content, Content with { Title = " " })));

        Assert.StartsWith("Lysbilde 2: title", problem);
    }

    [Fact]
    public void A_field_the_slide_kind_does_not_take_is_refused_naming_the_fields_it_does_take()
    {
        var problem = Assert.Single(PresentationSpecValidator.Validate(Spec(new SlideSpec(SlideKind.Chapter, "Kapittel", Text: "Ekstra"))));

        Assert.Contains("Feltene Chapter tar: title.", problem);
    }

    [Fact]
    public void A_list_with_more_bullets_than_the_limit_is_refused()
    {
        var bullets = Enumerable.Repeat("Punkt", PresentationSpecValidator.MaxBulletsPerList + 1).ToArray();

        Assert.Single(PresentationSpecValidator.Validate(Spec(Content with { Bullets = bullets })));
    }

    [Fact]
    public void An_empty_bullet_is_refused()
    {
        Assert.Single(PresentationSpecValidator.Validate(Spec(Content with { Bullets = ["Punkt", ""] })));
    }

    [Fact]
    public void Text_longer_than_the_limit_is_refused()
    {
        var spec = Spec(new SlideSpec(SlideKind.Quote, "Sitat", Text: new string('a', PresentationSpecValidator.MaxTextLength + 1)));

        Assert.Single(PresentationSpecValidator.Validate(spec));
    }

    [Fact]
    public void Control_characters_a_powerpoint_file_cannot_hold_are_refused()
    {
        Assert.Single(PresentationSpecValidator.Validate(Spec(Content with { Title = "Hoved\u0001tall" })));
    }

    [Fact]
    public void Emoji_are_allowed()
    {
        Assert.Empty(PresentationSpecValidator.Validate(Spec(Content with { Title = "Hovedtall 📈" })));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = PresentationSpecValidator.Validate(Spec(Content with { Title = "" }, new SlideSpec(SlideKind.Chapter, "K", Bullets: ["x"])));

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void A_null_slide_is_reported_naming_the_slide()
    {
        var problem = Assert.Single(PresentationSpecValidator.FindMissingSlides([Content, null]));

        Assert.StartsWith("Lysbilde 2", problem);
    }

    [Fact]
    public void Slides_that_are_all_present_have_none_missing()
    {
        Assert.Empty(PresentationSpecValidator.FindMissingSlides([Content, Content]));
    }
}

using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationSpecValidatorTests
{
    private static readonly string PictureId = PictureCatalog.Embedded.All[0].Id;

    private static readonly SlideSpec Content = new(SlideKind.Content, "Hovedtall", Bullets: ["Ett punkt"]);

    private static PresentationSpec Spec(params SlideSpec[] slides) => new("Tittel", PresentationTheme.SivStrand, slides);

    private static IReadOnlyList<string> Validate(PresentationSpec spec) => PresentationSpecValidator.Validate(spec, PictureCatalog.Embedded);

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

        Assert.Empty(Validate(spec));
    }

    [Fact]
    public void The_structure_check_accepts_a_spec_that_is_only_over_the_size_limits()
    {
        var spec = Spec([.. Enumerable.Repeat(new SlideSpec(SlideKind.Content, "Hovedtall", Bullets: [new string('x', PresentationSpecValidator.MaxBulletLength + 1)]), PresentationSpecValidator.MaxSlides + 1)]);

        Assert.Empty(PresentationSpecValidator.ValidateStructure(spec));
    }

    [Fact]
    public void The_structure_check_still_refuses_a_field_the_slide_kind_does_not_take()
    {
        var spec = Spec(new SlideSpec(SlideKind.Chapter, "Kapittel", Bullets: ["x"]));

        Assert.NotEmpty(PresentationSpecValidator.ValidateStructure(spec));
    }

    [Fact]
    public void A_presentation_without_slides_is_refused()
    {
        Assert.Single(Validate(Spec()));
    }

    [Fact]
    public void A_presentation_with_more_slides_than_the_limit_is_refused()
    {
        var slides = Enumerable.Repeat(Content, PresentationSpecValidator.MaxSlides + 1).ToArray();

        Assert.Single(Validate(Spec(slides)));
    }

    [Fact]
    public void A_slide_without_a_title_is_refused_naming_the_slide()
    {
        var problem = Assert.Single(Validate(Spec(Content, Content with { Title = " " })));

        Assert.StartsWith("Lysbilde 2: title", problem);
    }

    [Fact]
    public void A_field_the_slide_kind_does_not_take_is_refused_naming_the_fields_it_does_take()
    {
        var problem = Assert.Single(Validate(Spec(new SlideSpec(SlideKind.Chapter, "Kapittel", Text: "Ekstra"))));

        Assert.Contains("Feltene Chapter tar: title.", problem);
    }

    [Fact]
    public void A_list_with_more_bullets_than_the_limit_is_refused()
    {
        var bullets = Enumerable.Repeat("Punkt", PresentationSpecValidator.MaxBulletsPerList + 1).ToArray();

        Assert.Single(Validate(Spec(Content with { Bullets = bullets })));
    }

    [Fact]
    public void An_empty_bullet_is_refused()
    {
        Assert.Single(Validate(Spec(Content with { Bullets = ["Punkt", ""] })));
    }

    [Fact]
    public void Text_longer_than_the_limit_is_refused()
    {
        var spec = Spec(new SlideSpec(SlideKind.Quote, "Sitat", Text: new string('a', PresentationSpecValidator.MaxTextLength + 1)));

        Assert.Single(Validate(spec));
    }

    [Fact]
    public void Control_characters_a_powerpoint_file_cannot_hold_are_refused()
    {
        Assert.Single(Validate(Spec(Content with { Title = "Hoved\u0001tall" })));
    }

    [Fact]
    public void Emoji_are_allowed()
    {
        Assert.Empty(Validate(Spec(Content with { Title = "Hovedtall 📈" })));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = Validate(Spec(Content with { Title = "" }, new SlideSpec(SlideKind.Chapter, "K", Bullets: ["x"])));

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Every_picture_kind_with_a_catalog_picture_has_no_problems()
    {
        var spec = Spec(
            new(SlideKind.CoverWithPicture, "Forside", Text: "Undertittel", Bullets: ["Navn"], PictureId: PictureId),
            new(SlideKind.ChapterWithPicture, "Kapittel", PictureId: PictureId),
            new(SlideKind.ContentWithPicture, "Innhold", Bullets: ["Punkt"], PictureId: PictureId),
            new(SlideKind.FullscreenPicture, "Bilde", Text: "Bildetekst", PictureId: PictureId),
            new(SlideKind.ContentWithSmallPicture, "Innhold", Bullets: ["Punkt"], PictureId: PictureId, PictureFit: PictureFit.Contain),
            new(SlideKind.ContentWithSquarePicture, "Innhold", Bullets: ["Punkt"], PictureId: PictureId),
            new(SlideKind.TitleAndWidePicture, "Bilde", PictureId: PictureId, PictureFit: PictureFit.Cover),
            new(SlideKind.LargePicture, "Pause", PictureId: PictureId));

        Assert.Empty(Validate(spec));
    }

    [Fact]
    public void A_big_number_longer_than_its_limit_is_refused_naming_the_limit()
    {
        var problem = Assert.Single(Validate(Spec(new SlideSpec(SlideKind.BigNumber, "Andel", Text: new string('9', PresentationSpecValidator.MaxBigNumberLength + 1)))));

        Assert.Contains($"maks er {PresentationSpecValidator.MaxBigNumberLength}", problem);
    }

    [Fact]
    public void Other_kinds_text_may_be_longer_than_a_big_numbers()
    {
        Assert.Empty(Validate(Spec(new SlideSpec(SlideKind.Quote, "Sitat", Text: new string('x', PresentationSpecValidator.MaxBigNumberLength + 1)))));
    }

    [Fact]
    public void A_closing_title_longer_than_its_limit_is_refused()
    {
        Assert.Single(Validate(Spec(new SlideSpec(SlideKind.Closing, new string('x', PresentationSpecValidator.MaxClosingTitleLength + 1)))));
    }

    [Fact]
    public void A_picture_kind_without_a_picture_id_is_accepted_for_the_user_to_fill_in_powerpoint()
    {
        Assert.Empty(Validate(Spec(new SlideSpec(SlideKind.ContentWithSmallPicture, "Innhold", Bullets: ["Punkt"]))));
    }

    [Fact]
    public void A_picture_fit_on_a_kind_without_a_picture_is_refused_naming_the_field()
    {
        var problem = Assert.Single(Validate(Spec(Content with { PictureFit = PictureFit.Contain })));

        Assert.Contains("pictureFit", problem);
    }

    [Fact]
    public void An_empty_picture_id_is_refused()
    {
        Assert.Single(Validate(Spec(new SlideSpec(SlideKind.ChapterWithPicture, "Kapittel", PictureId: " "))));
    }

    [Fact]
    public void A_picture_id_that_is_not_in_the_catalog_is_refused_naming_the_id()
    {
        var problem = Assert.Single(Validate(Spec(new SlideSpec(SlideKind.ChapterWithPicture, "Kapittel", PictureId: "finnes-ikke"))));

        Assert.Contains("finnes-ikke", problem);
    }

    [Fact]
    public void A_picture_id_on_a_kind_without_a_picture_is_refused()
    {
        Assert.Single(Validate(Spec(new SlideSpec(SlideKind.Chapter, "Kapittel", PictureId: PictureId))));
    }

    [Fact]
    public void The_structure_check_accepts_a_picture_that_has_left_the_catalog()
    {
        var spec = Spec(new SlideSpec(SlideKind.ChapterWithPicture, "Kapittel", PictureId: "fjernet-bilde"));

        Assert.Empty(PresentationSpecValidator.ValidateStructure(spec));
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

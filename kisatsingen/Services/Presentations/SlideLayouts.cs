namespace kisatsingen.Services.Presentations;

// Where each slide kind's fields go in the template: the layout, by its name
// in the template, and the placeholder idx for each field. Null means the
// kind takes no such field. Every slide's title goes to the layout's title
// placeholder.
//
// By name rather than by number, so the mapping survives the template's
// layouts being reordered. Names repeat across the four masters, so a name is
// looked up within the chosen theme's master.
internal sealed record SlideLayout(string LayoutName, uint? TextIndex, uint? BulletsIndex, uint? SecondColumnIndex, uint? PictureIndex = null);

internal static class SlideLayouts
{
    private const uint TemplatePictureIndex = 16;
    private const uint TemplateIconPictureIndex = 32;

    private static readonly IReadOnlyDictionary<SlideKind, SlideLayout> ByKind = new Dictionary<SlideKind, SlideLayout>
    {
        [SlideKind.Cover] = new("Forside - Kun tekst 1", TextIndex: 23, BulletsIndex: 22, SecondColumnIndex: null),
        [SlideKind.Agenda] = new("Agenda", TextIndex: null, BulletsIndex: 25, SecondColumnIndex: null),
        [SlideKind.Chapter] = new("Kapittelinndeler - Tekst", TextIndex: null, BulletsIndex: null, SecondColumnIndex: null),
        [SlideKind.Content] = new("Tittel og innhold 1 kolonne", TextIndex: null, BulletsIndex: 1, SecondColumnIndex: null),
        [SlideKind.TwoColumns] = new("Tittel og innhold 2 kolonner", TextIndex: null, BulletsIndex: 1, SecondColumnIndex: 14),
        [SlideKind.Quote] = new("Stort sitat", TextIndex: 23, BulletsIndex: null, SecondColumnIndex: null),
        [SlideKind.BigNumber] = new("Tall i sirkel", TextIndex: 10, BulletsIndex: null, SecondColumnIndex: null),
        [SlideKind.Closing] = new("Avsluttende lysbilde", TextIndex: 16, BulletsIndex: null, SecondColumnIndex: null),
        [SlideKind.CoverWithPicture] = new("Forside - Tekst og bilde 1", TextIndex: 23, BulletsIndex: 22, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.ChapterWithPicture] = new("Kapittelinndeler - Tekst og bilde", TextIndex: null, BulletsIndex: null, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.ContentWithPicture] = new("Tittel og innhold - Bilde høyre", TextIndex: null, BulletsIndex: 1, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.FullscreenPicture] = new("Fullskjerm bilde med tekst", TextIndex: 33, BulletsIndex: null, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.ContentWithSmallPicture] = new("Tittel og innhold med bilde", TextIndex: null, BulletsIndex: 1, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.ContentWithSquarePicture] = new("Tittel og innhold med valgfritt ikon", TextIndex: null, BulletsIndex: 1, SecondColumnIndex: null, PictureIndex: TemplateIconPictureIndex),
        [SlideKind.TitleAndWidePicture] = new("Tittel og bilde", TextIndex: null, BulletsIndex: null, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex),
        [SlideKind.LargePicture] = new("Pausebilde", TextIndex: null, BulletsIndex: null, SecondColumnIndex: null, PictureIndex: TemplatePictureIndex)
    };

    public static SlideLayout For(SlideKind kind) =>
        ByKind.TryGetValue(kind, out var layout)
            ? layout
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Slide kind {kind} has no layout. Add it to {nameof(SlideLayouts)}.");
}

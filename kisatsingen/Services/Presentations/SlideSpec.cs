using System.ComponentModel;

namespace kisatsingen.Services.Presentations;

// Flat rather than one type per kind: models fill a flat shape more reliably.
// Which fields a kind takes is decided by SlideLayouts and enforced by
// PresentationSpecValidator.
public sealed record SlideSpec(
    [property: Description(
        "The slide's layout. Cover: the first slide. Agenda: the list of topics. Chapter: a section divider with only a title. " +
        "Content: a title and bullets. TwoColumns: a title and two bullet columns. Quote: a large quote. " +
        "BigNumber: one key figure in a circle. Closing: the last slide. " +
        "CoverWithPicture: as Cover, with a square picture beside the text. ChapterWithPicture: as Chapter, with a tall picture at the right edge. " +
        "ContentWithPicture: as Content, with a picture filling the right half of the slide edge to edge. " +
        "ContentWithSmallPicture: as Content, with a smaller picture beside the bullets and the background around it. " +
        "ContentWithSquarePicture: as Content, with a square picture beside the bullets. " +
        "TitleAndWidePicture: a title and a wide picture centred under it. LargePicture: a short title over a large centred picture. " +
        "FullscreenPicture: a picture filling the slide, with one short line of text.")]
    SlideKind Kind,
    [property: Description(
        "The slide title; required on every slide for accessibility. For Quote it is the quote itself. " +
        "For BigNumber and FullscreenPicture it is hidden on screen but read by screen readers; for BigNumber describe the figure. " +
        "For Closing keep it to one short line.")]
    string Title,
    [property: Description(
        "One extra line, used only by Cover and CoverWithPicture (subtitle), Quote (who said it), BigNumber (the figure, at most 8 characters, e.g. \"87 %\"), " +
        "FullscreenPicture (a short caption) and Closing (contact, e.g. an e-mail address).")]
    string? Text = null,
    [property: Description(
        "Bullet points, one short phrase each, used only by Cover and CoverWithPicture (name, date, place), Agenda (topics), " +
        "Content, ContentWithPicture, ContentWithSmallPicture, ContentWithSquarePicture and TwoColumns (left column).")]
    IReadOnlyList<string>? Bullets = null,
    [property: Description("The right column's bullet points, used only by TwoColumns.")]
    IReadOnlyList<string>? SecondColumn = null,
    [property: Description(
        "The picture's id from list_presentation_pictures; used only by the picture kinds. Never make up an id. " +
        "Leave it out only when the user says they will add the pictures themselves: the slide then gets an empty picture frame to fill in PowerPoint.")]
    string? PictureId = null,
    [property: Description(
        "How the picture meets its frame, used only by the picture kinds. Cover (the default): fills the frame, cropping the edges that do not fit. " +
        "Contain: shows the whole picture, scaled down to fit, with the background around it; use it when nothing may be cut off, " +
        "such as a portrait picture in a wide frame or a picture with text or a logo near its edge.")]
    PictureFit? PictureFit = null);

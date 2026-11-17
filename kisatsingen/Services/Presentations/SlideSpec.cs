using System.ComponentModel;

namespace kisatsingen.Services.Presentations;

// Flat rather than one type per kind: models fill a flat shape more reliably.
// Which fields a kind takes is decided by SlideLayouts and enforced by
// PresentationSpecValidator.
public sealed record SlideSpec(
    [property: Description(
        "The slide's layout. Cover: the first slide. Agenda: the list of topics. Chapter: a section divider with only a title. " +
        "Content: a title and bullets. TwoColumns: a title and two bullet columns. Quote: a large quote. " +
        "BigNumber: one key figure in a circle. Closing: the last slide.")]
    SlideKind Kind,
    [property: Description(
        "The slide title; required on every slide for accessibility. For Quote it is the quote itself. " +
        "For BigNumber it is hidden on screen but read by screen readers, so describe the figure.")]
    string Title,
    [property: Description(
        "One extra line, used only by Cover (subtitle), Quote (who said it), BigNumber (the figure, e.g. \"87 %\") and Closing (contact, e.g. an e-mail address).")]
    string? Text = null,
    [property: Description(
        "Bullet points, one short phrase each, used only by Cover (name, date, place), Agenda (topics), Content and TwoColumns (left column).")]
    IReadOnlyList<string>? Bullets = null,
    [property: Description("The right column's bullet points, used only by TwoColumns.")]
    IReadOnlyList<string>? SecondColumn = null);

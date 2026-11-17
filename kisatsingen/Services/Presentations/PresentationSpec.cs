namespace kisatsingen.Services.Presentations;

// Title becomes the file's document title (an accessibility requirement) and its file name.
public sealed record PresentationSpec(string Title, PresentationTheme Theme, IReadOnlyList<SlideSpec> Slides);

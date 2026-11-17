using kisatsingen.Services.Presentations;

namespace kisatsingen.Components.Presentation;

// A presentation a tool call generated, as the download button offers it.
public sealed record GeneratedPresentation(string FileName, PresentationSpec Spec);

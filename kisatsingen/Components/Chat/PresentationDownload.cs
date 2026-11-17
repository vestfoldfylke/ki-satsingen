using kisatsingen.Services.Presentations;

namespace kisatsingen.Components.Chat;

// A presentation a tool call made, as the download button offers it.
public sealed record PresentationDownload(string FileName, PresentationSpec Spec);

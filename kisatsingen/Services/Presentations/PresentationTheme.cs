using System.Text.Json.Serialization;

namespace kisatsingen.Services.Presentations;

// The template's four colour themes, one slide master each. The order matches
// the masters' order in the template, which SlideLayouts relies on. What each
// looks like is told to the model in CreatePresentationTool's theme parameter.
[JsonConverter(typeof(JsonStringEnumConverter<PresentationTheme>))]
public enum PresentationTheme
{
    SivStrand,
    VannFjord,
    BarkNype,
    GranKorn
}

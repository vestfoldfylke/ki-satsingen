using System.ComponentModel;
using System.Text.Json.Serialization;

namespace kisatsingen.Services.Presentations;

// The template's four colour themes, one slide master each. The order matches
// the masters' order in the template, which SlideLayouts relies on.
[JsonConverter(typeof(JsonStringEnumConverter<PresentationTheme>))]
public enum PresentationTheme
{
    [Description("Siv/Strand: sand and beige. The default.")]
    SivStrand,

    [Description("Vann/Fjord: blue.")]
    VannFjord,

    [Description("Bark/Nype: warm bark and rose.")]
    BarkNype,

    [Description("Gran/Korn: green and grain.")]
    GranKorn
}

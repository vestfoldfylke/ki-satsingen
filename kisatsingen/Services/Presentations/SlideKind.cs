using System.Text.Json.Serialization;

namespace kisatsingen.Services.Presentations;

// The template layouts a slide can use. A curated, text-only subset: pasted
// text has no pictures to fill the template's image layouts with.
[JsonConverter(typeof(JsonStringEnumConverter<SlideKind>))]
public enum SlideKind
{
    Cover,
    Agenda,
    Chapter,
    Content,
    TwoColumns,
    Quote,
    BigNumber,
    Closing
}

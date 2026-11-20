using System.Text.Json.Serialization;

namespace kisatsingen.Services.Presentations;

// The template layouts a slide can use. A curated subset: the text layouts,
// and the picture layouts that take one picture from the PictureCatalog.
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
    Closing,
    CoverWithPicture,
    ChapterWithPicture,
    ContentWithPicture,
    FullscreenPicture,
    ContentWithSmallPicture,
    ContentWithSquarePicture,
    TitleAndWidePicture,
    LargePicture
}

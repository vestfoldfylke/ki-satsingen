using System.Text.Json.Serialization;

namespace kisatsingen.Services.Presentations;

// How a picture meets its placeholder. Named after CSS object-fit, which the
// models know well.
[JsonConverter(typeof(JsonStringEnumConverter<PictureFit>))]
public enum PictureFit
{
    Cover,
    Contain
}

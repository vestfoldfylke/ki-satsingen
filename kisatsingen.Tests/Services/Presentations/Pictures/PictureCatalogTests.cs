using System.Text;
using kisatsingen.Services.Presentations.Pictures;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations.Pictures;

public sealed class PictureCatalogTests
{
    private const string PictureFile = "bilde.jpg";

    // The smallest header ImageHeaderReader accepts: SOI, then a SOF0 frame
    // giving 3×2 pixels.
    private static readonly byte[] TinyJpeg =
        [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x02, 0x00, 0x03, 0x01, 0x01, 0x11, 0x00];

    [Fact]
    public void Every_embedded_picture_loads_with_its_size_and_alt_text()
    {
        var pictures = PictureCatalog.Embedded.All;

        Assert.NotEmpty(pictures);
        Assert.All(pictures, picture => Assert.True(
            picture.Dimensions is { Width: > 0, Height: > 0 } && !string.IsNullOrWhiteSpace(picture.AltText) && picture.Bytes.Length > 0,
            picture.Id));
    }

    [Fact]
    public void A_picture_is_found_by_its_id()
    {
        var catalog = Load(Manifest(Entry("fjord")), withPicture: true);

        Assert.Equal((3L, 2L), (catalog.Find("fjord")!.Dimensions.Width, catalog.Find("fjord")!.Dimensions.Height));
    }

    [Fact]
    public void An_unknown_id_finds_no_picture()
    {
        var catalog = Load(Manifest(Entry("fjord")), withPicture: true);

        Assert.Null(catalog.Find("skog"));
    }

    [Fact]
    public void A_manifest_entry_whose_file_is_not_embedded_stops_loading_naming_the_file()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(Manifest(Entry("fjord")), withPicture: false));

        Assert.Contains(PictureFile, error.Message);
    }

    [Fact]
    public void A_repeated_picture_id_stops_loading_naming_the_id()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(Manifest(Entry("fjord"), Entry("fjord")), withPicture: true));

        Assert.Contains("\"fjord\"", error.Message);
    }

    [Fact]
    public void A_picture_id_with_capitals_or_spaces_stops_loading()
    {
        Assert.Throws<InvalidOperationException>(() => Load(Manifest(Entry("Fjord og hav")), withPicture: true));
    }

    [Fact]
    public void A_picture_without_alt_text_stops_loading_naming_the_field()
    {
        var entry = $$"""{ "id": "fjord", "file": "{{PictureFile}}", "altText": " ", "description": "Fjord" }""";

        var error = Assert.Throws<InvalidOperationException>(() => Load(Manifest(entry), withPicture: true));

        Assert.Contains("altText", error.Message);
    }

    [Fact]
    public void A_file_that_is_not_a_readable_image_stops_loading()
    {
        Assert.Throws<InvalidOperationException>(() => PictureCatalog.Load(name => name == PictureCatalog.ManifestResourceName
            ? Utf8(Manifest(Entry("fjord")))
            : new MemoryStream([0xFF, 0xD8, 0xFF])));
    }

    private static PictureCatalog Load(string manifest, bool withPicture) =>
        PictureCatalog.Load(name => name switch
        {
            PictureCatalog.ManifestResourceName => Utf8(manifest),
            PictureCatalog.ResourcePrefix + PictureFile when withPicture => new MemoryStream(TinyJpeg),
            _ => null
        });

    private static string Entry(string id) =>
        $$"""{ "id": "{{id}}", "file": "{{PictureFile}}", "altText": "En fjord", "description": "A fjord" }""";

    private static string Manifest(params string[] entries) => $"[{string.Join(",", entries)}]";

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));
}

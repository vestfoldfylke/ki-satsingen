using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Tests.Services.Presentations;

// Against a catalog of known sizes, so crops and placements can be checked
// whatever pictures the real catalog holds.
public sealed class VestfoldPresentationBuilderPictureTests
{
    private const string WidePicture = "bred";
    private const string TallPicture = "hoy";
    private const string AltText = "Et testbilde";
    private const uint TemplatePictureIndex = 16;

    private static readonly PictureCatalog Catalog = PictureCatalog.Load(name => name switch
    {
        PictureCatalog.ManifestResourceName => new MemoryStream(System.Text.Encoding.UTF8.GetBytes($$"""
            [
              { "id": "{{WidePicture}}", "file": "bred.jpg", "altText": "{{AltText}}", "description": "Wide" },
              { "id": "{{TallPicture}}", "file": "hoy.jpg", "altText": "{{AltText}}", "description": "Tall" }
            ]
            """)),
        PictureCatalog.ResourcePrefix + "bred.jpg" => new MemoryStream(JpegHeader(width: 300, height: 100)),
        PictureCatalog.ResourcePrefix + "hoy.jpg" => new MemoryStream(JpegHeader(width: 100, height: 300)),
        _ => null
    });

    [Fact]
    public void A_picture_slide_fills_its_layouts_picture_placeholder_with_the_catalog_picture_and_its_alt_text()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.ChapterWithPicture, "Kapittel", PictureId: WidePicture));

        var picture = Assert.Single(slide.Slide!.Descendants<Picture>());
        var image = Assert.Single(slide.ImageParts);
        var placeholder = picture.Descendants<PlaceholderShape>().Single();

        Assert.Equal(
            ("pic", TemplatePictureIndex, AltText, slide.GetIdOfPart(image)),
            (placeholder.Type?.InnerText, placeholder.Index?.Value, picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value, picture.BlipFill?.Blip?.Embed?.Value));
        Assert.Equal(Catalog.Find(WidePicture)!.Bytes, ReadAll(image));
    }

    [Fact]
    public void A_covering_picture_wider_than_its_frame_is_cropped_equally_from_both_sides_and_keeps_the_layouts_position()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.ContentWithPicture, "Innhold", PictureId: WidePicture));

        var picture = Assert.Single(slide.Slide!.Descendants<Picture>());
        var crop = picture.Descendants<A.SourceRectangle>().Single();

        Assert.True(crop.Left?.Value > 0 && crop.Left.Value == crop.Right?.Value && crop.Top is null && crop.Bottom is null, crop.OuterXml);
        Assert.Null(picture.ShapeProperties?.Transform2D);
    }

    [Fact]
    public void A_contained_picture_is_shown_whole_at_the_frames_height_and_centred_across_it()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.ContentWithSmallPicture, "Innhold", PictureId: TallPicture, PictureFit: PictureFit.Contain));
        var frame = LayoutFrame(slide, placeholderIndex: TemplatePictureIndex);

        var picture = Assert.Single(slide.Slide!.Descendants<Picture>());
        var placed = picture.ShapeProperties!.Transform2D!;
        var (x, y, width, height) = (placed.Offset!.X!.Value, placed.Offset.Y!.Value, placed.Extents!.Cx!.Value, placed.Extents.Cy!.Value);

        Assert.Empty(picture.Descendants<A.SourceRectangle>().Single().GetAttributes());
        Assert.Equal((frame.Offset!.Y!.Value, frame.Extents!.Cy!.Value), (y, height));
        Assert.InRange(width * 3 - height, -3, 3);
        Assert.InRange(x - frame.Offset.X!.Value - (frame.Extents.Cx!.Value - width) / 2, -1, 1);
    }

    [Fact]
    public void A_picture_slide_without_a_picture_id_has_an_empty_picture_frame_to_fill_in_powerpoint()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.ContentWithSmallPicture, "Innhold", Bullets: ["Punkt"]));

        Assert.Equal((0, 0, TemplatePictureIndex), (slide.Slide!.Descendants<Picture>().Count(), slide.ImageParts.Count(), EmptyPictureFrameIndex(slide)));
    }

    [Fact]
    public void A_picture_that_has_left_the_catalog_leaves_its_placeholder_empty_rather_than_failing()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.ChapterWithPicture, "Kapittel", PictureId: "fjernet-bilde"));

        Assert.Equal((0, 0, TemplatePictureIndex), (slide.Slide!.Descendants<Picture>().Count(), slide.ImageParts.Count(), EmptyPictureFrameIndex(slide)));
    }

    [Fact]
    public void A_picture_slide_is_read_title_first_and_draws_its_caption_in_front_of_the_picture()
    {
        var slide = BuildSingle(new SlideSpec(SlideKind.FullscreenPicture, "Bilde", Text: "Bildetekst", PictureId: WidePicture));

        var shapeOrder = slide.Slide!.CommonSlideData!.ShapeTree!.Descendants<PlaceholderShape>().Select(placeholder => placeholder.Type?.InnerText);

        Assert.Equal(["title", "pic", "body"], shapeOrder);
    }

    // SOI, then a SOF0 frame header: all ImageHeaderReader reads.
    private static byte[] JpegHeader(ushort width, ushort height) =>
        [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00];

    private static SlidePart BuildSingle(SlideSpec slide)
    {
        var bytes = new VestfoldPresentationBuilder(new PresentationTemplate(), Catalog, TimeProvider.System)
            .Build(new PresentationSpec("Bilder", PresentationTheme.SivStrand, [slide]))
            .ToArray();
        var document = PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);
        return Assert.Single(document.PresentationPart!.SlideParts);
    }

    private static uint? EmptyPictureFrameIndex(SlidePart slide) =>
        slide.Slide!.Descendants<Shape>()
            .Select(shape => shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape)
            .Single(placeholder => placeholder?.Type?.InnerText == "pic")?.Index?.Value;

    private static A.Transform2D LayoutFrame(SlidePart slide, uint placeholderIndex) =>
        slide.SlideLayoutPart!.SlideLayout!.Descendants<PlaceholderShape>()
            .Single(placeholder => placeholder.Index?.Value == placeholderIndex)
            .Ancestors<Shape>().First().ShapeProperties!.Transform2D!;

    private static byte[] ReadAll(ImagePart image)
    {
        using var stream = image.GetStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

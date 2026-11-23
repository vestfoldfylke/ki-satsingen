using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Services.Presentations.Pictures;

// Fills a layout's picture placeholder, never stretching the picture. Cover
// takes the layout's position and size and crops the picture to the frame's
// shape, the way PowerPoint fills a picture placeholder. Contain gives the
// picture its own, smaller size, centred in the frame, so the slide's
// background shows around it.
internal static class PictureShapeFactory
{
    public static Picture Create(SlidePart slidePart, uint id, PlaceholderShape layoutPlaceholder, CatalogPicture picture, PictureFit fit)
    {
        var imagePart = slidePart.AddImagePart(picture.ContentType);
        using (var stream = new MemoryStream(picture.Bytes, writable: false))
        {
            imagePart.FeedData(stream);
        }

        var placeholder = new PlaceholderShape { Type = PlaceholderValues.Picture };
        if (layoutPlaceholder.Index is { } index)
        {
            placeholder.Index = index.Value;
        }

        var frame = FrameOf(layoutPlaceholder);
        var (crop, shapeProperties) = fit == PictureFit.Contain && frame is not null
            ? (PictureCrop.None, new ShapeProperties(ContainedTransform(frame, picture)))
            : (CropFor(frame, picture), new ShapeProperties());

        return new Picture(
            new NonVisualPictureProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Bilde {id}", Description = picture.AltText },
                new NonVisualPictureDrawingProperties(new A.PictureLocks { NoGrouping = true, NoChangeAspect = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new BlipFill(
                new A.Blip { Embed = slidePart.GetIdOfPart(imagePart) },
                CreateSourceRectangle(crop),
                new A.Stretch(new A.FillRectangle())),
            shapeProperties);
    }

    private sealed record Frame(long X, long Y, long Width, long Height);

    // A placeholder without its own position inherits it from the master,
    // which the template's picture placeholders never do; the picture is then
    // left uncropped and unplaced, rather than guessing a frame.
    private static Frame? FrameOf(PlaceholderShape layoutPlaceholder) =>
        layoutPlaceholder.Ancestors<Shape>().FirstOrDefault()?.ShapeProperties?.Transform2D is
        {
            Offset: { X.Value: var x, Y.Value: var y },
            Extents: { Cx.Value: var width, Cy.Value: var height }
        }
            ? new Frame(x, y, width, height)
            : null;

    private static PictureCrop CropFor(Frame? frame, CatalogPicture picture) =>
        frame is null
            ? PictureCrop.None
            : PictureCrop.ForCover(picture.Dimensions.Width, picture.Dimensions.Height, frame.Width, frame.Height);

    private static A.Transform2D ContainedTransform(Frame frame, CatalogPicture picture)
    {
        var placement = PicturePlacement.Contain(picture.Dimensions.Width, picture.Dimensions.Height, frame.X, frame.Y, frame.Width, frame.Height);
        return new A.Transform2D(
            new A.Offset { X = placement.X, Y = placement.Y },
            new A.Extents { Cx = placement.Width, Cy = placement.Height });
    }

    private static A.SourceRectangle CreateSourceRectangle(PictureCrop crop)
    {
        var rectangle = new A.SourceRectangle();
        if (crop.Left != 0)
        {
            rectangle.Left = crop.Left;
        }
        if (crop.Top != 0)
        {
            rectangle.Top = crop.Top;
        }
        if (crop.Right != 0)
        {
            rectangle.Right = crop.Right;
        }
        if (crop.Bottom != 0)
        {
            rectangle.Bottom = crop.Bottom;
        }
        return rectangle;
    }
}

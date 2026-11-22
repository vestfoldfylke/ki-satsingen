using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using kisatsingen.Services.Presentations.Pictures;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Services.Presentations;

// Builds one slide on a template layout. Only the placeholders are written:
// position, font, colour and bullets all come from the layout, so the slide
// looks exactly as the template intends. The one exception is the font size
// of text too long for its box at the template's size; see TextFit.
internal static class SlideFactory
{
    private const string TextLanguage = "nb-NO";
    private const uint GroupShapeId = 1;
    private const uint FirstShapeId = 2;

    // A picture slide can be missing its picture, either because the user
    // wants to add it in PowerPoint, or because the picture was removed from
    // the catalog after the chat was saved. The slide then gets an empty
    // picture frame instead of failing.
    public static SlidePart Create(PresentationPart presentationPart, SlideLayoutPart layoutPart, SlideSpec spec, CatalogPicture? picture)
    {
        var layout = SlideLayouts.For(spec.Kind);
        var layoutPlaceholders = layoutPart.SlideLayout?.CommonSlideData?.ShapeTree?.Descendants<PlaceholderShape>().ToList() ?? [];

        var titlePlaceholder = FindTitle(layoutPlaceholders, layout);
        var bodyFills = new List<(PlaceholderShape Placeholder, IReadOnlyList<string> Paragraphs)>();
        AddFill(bodyFills, layoutPlaceholders, layout, layout.TextIndex, spec.Text is { } text ? [text] : null);
        AddFill(bodyFills, layoutPlaceholders, layout, layout.BulletsIndex, spec.Bullets);
        AddFill(bodyFills, layoutPlaceholders, layout, layout.SecondColumnIndex, spec.SecondColumn);

        var shapeTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = GroupShapeId, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));

        var masterPart = layoutPart.SlideMasterPart
            ?? throw new InvalidOperationException($"Layout \"{layout.LayoutName}\" has no slide master; the template file is damaged.");

        // Before the slide is set, since the picture's image part hangs off
        // the slide part.
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        var shapeId = FirstShapeId;

        // Screen readers read the shapes in this order, and PowerPoint draws
        // later shapes on top. The title goes first so it is read first (no
        // layout puts a picture over the title). The picture goes before the
        // other text, so a caption on top of it stays visible.
        shapeTree.Append(CreatePlaceholderShape(shapeId++, titlePlaceholder, [spec.Title], masterPart));

        if (layout.PictureIndex is { } pictureIndex)
        {
            var picturePlaceholder = FindPlaceholder(layoutPlaceholders, layout, pictureIndex);
            OpenXmlElement pictureShape = picture is null
                ? CreateEmptyPicturePlaceholder(shapeId++, picturePlaceholder)
                : PictureShapeFactory.Create(slidePart, shapeId++, picturePlaceholder, picture, spec.PictureFit ?? PictureFit.Cover);
            shapeTree.Append(pictureShape);
        }

        foreach (var (placeholder, paragraphs) in bodyFills)
        {
            shapeTree.Append(CreatePlaceholderShape(shapeId++, placeholder, paragraphs, masterPart));
        }

        slidePart.Slide = new Slide(
            new CommonSlideData(shapeTree),
            new ColorMapOverride(new A.MasterColorMapping()));
        slidePart.AddPart(layoutPart);
        return slidePart;
    }

    private static void AddFill(
        List<(PlaceholderShape, IReadOnlyList<string>)> fills,
        List<PlaceholderShape> layoutPlaceholders,
        SlideLayout layout,
        uint? index,
        IReadOnlyList<string>? paragraphs)
    {
        if (index is not { } placeholderIndex || paragraphs is null)
        {
            return;
        }

        fills.Add((FindPlaceholder(layoutPlaceholders, layout, placeholderIndex), paragraphs));
    }

    private static PlaceholderShape FindPlaceholder(List<PlaceholderShape> layoutPlaceholders, SlideLayout layout, uint placeholderIndex) =>
        layoutPlaceholders.FirstOrDefault(p => p.Index?.Value == placeholderIndex)
        ?? throw new InvalidOperationException(
            $"Layout \"{layout.LayoutName}\" has no placeholder with idx {placeholderIndex}. The template changed; update {nameof(SlideLayouts)} to match it.");

    private static PlaceholderShape FindTitle(List<PlaceholderShape> layoutPlaceholders, SlideLayout layout) =>
        layoutPlaceholders.FirstOrDefault(p => p.Type?.Value == PlaceholderValues.Title || p.Type?.Value == PlaceholderValues.CenteredTitle)
        ?? throw new InvalidOperationException(
            $"Layout \"{layout.LayoutName}\" has no title placeholder. The template changed; update {nameof(SlideLayouts)} to match it.");

    // The placeholder reference repeats the layout's type and idx, which is
    // how PowerPoint ties the slide's shape to the layout's.
    // The font size, in hundredths of a point, is set only on text shrunk to
    // fit; otherwise the layout's size applies.
    private static Shape CreatePlaceholderShape(uint id, PlaceholderShape layoutPlaceholder, IReadOnlyList<string> paragraphs, SlideMasterPart masterPart)
    {
        var fontSize = PlaceholderTextBoxes.Resolve(layoutPlaceholder, masterPart) is { } box ? TextFit.FontSizeToFit(paragraphs, box) : null;

        var placeholder = new PlaceholderShape();
        if (layoutPlaceholder.Type is { } type)
        {
            placeholder.Type = type.Value;
        }
        if (layoutPlaceholder.Index is { } index)
        {
            placeholder.Index = index.Value;
        }

        var textBody = new TextBody(new A.BodyProperties(), new A.ListStyle());
        textBody.Append(paragraphs.Select(text => CreateParagraph(text, fontSize)));

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Plassholder {id}" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new ShapeProperties(),
            textBody);
    }

    // PowerPoint shows a layout placeholder only where the slide has a shape
    // referring to it; this one gives the user a frame to click and fill.
    private static Shape CreateEmptyPicturePlaceholder(uint id, PlaceholderShape layoutPlaceholder)
    {
        var placeholder = new PlaceholderShape { Type = PlaceholderValues.Picture };
        if (layoutPlaceholder.Index is { } index)
        {
            placeholder.Index = index.Value;
        }

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Plassholder for bilde {id}" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new ShapeProperties());
    }

    private static A.Paragraph CreateParagraph(string text, int? fontSize)
    {
        var runProperties = new A.RunProperties { Language = TextLanguage, Dirty = false };
        if (fontSize is { } size)
        {
            runProperties.FontSize = size;
        }

        return new A.Paragraph(new A.Run(runProperties, new A.Text(text)));
    }
}

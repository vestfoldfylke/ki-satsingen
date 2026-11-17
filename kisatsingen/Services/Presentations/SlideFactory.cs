using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Services.Presentations;

// Builds one slide on a template layout. Only the placeholders are written:
// position, font, colour and bullets all come from the layout, so the slide
// looks exactly as the template intends.
internal static class SlideFactory
{
    private const string TextLanguage = "nb-NO";
    private const uint GroupShapeId = 1;
    private const uint FirstShapeId = 2;

    public static SlidePart Create(PresentationPart presentationPart, SlideLayoutPart layoutPart, SlideSpec spec)
    {
        var layout = SlideLayouts.For(spec.Kind);
        var layoutPlaceholders = layoutPart.SlideLayout?.CommonSlideData?.ShapeTree?.Descendants<PlaceholderShape>().ToList() ?? [];

        var fills = new List<(PlaceholderShape Placeholder, IReadOnlyList<string> Paragraphs)>
        {
            (FindTitle(layoutPlaceholders, layout), [spec.Title])
        };
        AddFill(fills, layoutPlaceholders, layout, layout.TextIndex, spec.Text is { } text ? [text] : null);
        AddFill(fills, layoutPlaceholders, layout, layout.BulletsIndex, spec.Bullets);
        AddFill(fills, layoutPlaceholders, layout, layout.SecondColumnIndex, spec.SecondColumn);

        var shapeTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = GroupShapeId, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));

        var shapeId = FirstShapeId;
        foreach (var (placeholder, paragraphs) in fills)
        {
            shapeTree.Append(CreatePlaceholderShape(shapeId++, placeholder, paragraphs));
        }

        var slidePart = presentationPart.AddNewPart<SlidePart>();
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

        var placeholder = layoutPlaceholders.FirstOrDefault(p => p.Index?.Value == placeholderIndex)
            ?? throw new InvalidOperationException(
                $"Layout \"{layout.LayoutName}\" has no placeholder with idx {placeholderIndex}. The template changed; update {nameof(SlideLayouts)} to match it.");
        fills.Add((placeholder, paragraphs));
    }

    private static PlaceholderShape FindTitle(List<PlaceholderShape> layoutPlaceholders, SlideLayout layout) =>
        layoutPlaceholders.FirstOrDefault(p => p.Type?.Value == PlaceholderValues.Title || p.Type?.Value == PlaceholderValues.CenteredTitle)
        ?? throw new InvalidOperationException(
            $"Layout \"{layout.LayoutName}\" has no title placeholder. The template changed; update {nameof(SlideLayouts)} to match it.");

    // The placeholder reference repeats the layout's type and idx, which is
    // how PowerPoint ties the slide's shape to the layout's.
    private static Shape CreatePlaceholderShape(uint id, PlaceholderShape layoutPlaceholder, IReadOnlyList<string> paragraphs)
    {
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
        textBody.Append(paragraphs.Select(CreateParagraph));

        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Plassholder {id}" },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new ShapeProperties(),
            textBody);
    }

    private static A.Paragraph CreateParagraph(string text) =>
        new(new A.Run(
            new A.RunProperties { Language = TextLanguage, Dirty = false },
            new A.Text(text)));
}

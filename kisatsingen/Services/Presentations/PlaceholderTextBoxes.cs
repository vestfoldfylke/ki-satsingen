using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace kisatsingen.Services.Presentations;

// Reads a layout placeholder's text box the way PowerPoint resolves it: what
// the layout sets, else the master's placeholder of the same kind, else the
// master's title or body text style. Only the first paragraph level, since
// the slides never indent.
internal static class PlaceholderTextBoxes
{
    private const double EmuPerPoint = 12_700;
    private const double HundredthsPerPoint = 100;

    // DrawingML's defaults when no level sets an inset: 0.1" left and right,
    // 0.05" top and bottom.
    private const long DefaultHorizontalInset = 91_440;
    private const long DefaultVerticalInset = 45_720;

    // An ellipse's text area is the rectangle inscribed in it: cos 45° of its
    // width and height, as DrawingML's preset geometry defines it. BigNumber's
    // circle is the template's only text placeholder that is not a rectangle.
    private const double EllipseTextAreaShare = 0.70711;

    // Null when the template leaves the size or the font size unset, which it
    // never does for the layouts SlideLayouts names; such text is left as is.
    public static TextBox? Resolve(PlaceholderShape layoutPlaceholder, SlideMasterPart masterPart)
    {
        var layoutShape = layoutPlaceholder.Ancestors<Shape>().FirstOrDefault();
        var isTitle = layoutPlaceholder.Type?.Value is { } type && (type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle);
        var masterShape = FindMasterShape(masterPart, isTitle);
        Shape?[] shapes = [layoutShape, masterShape];

        var extents = shapes.Select(shape => shape?.ShapeProperties?.Transform2D?.Extents).FirstOrDefault(found => found is not null);
        var fontSize = shapes.Select(FontSizeOf).FirstOrDefault(found => found is not null) ?? MasterStyleFontSize(masterPart, isTitle);
        if (extents is not { Cx.Value: var width, Cy.Value: var height } || fontSize is not { } size)
        {
            return null;
        }

        var textAreaShare = IsEllipse(shapes) ? EllipseTextAreaShare : 1;
        var horizontalInsets = Inset(shapes, body => body.LeftInset, DefaultHorizontalInset) + Inset(shapes, body => body.RightInset, DefaultHorizontalInset);
        var verticalInsets = Inset(shapes, body => body.TopInset, DefaultVerticalInset) + Inset(shapes, body => body.BottomInset, DefaultVerticalInset);

        return new TextBox(
            WidthPoints: (width * textAreaShare - horizontalInsets) / EmuPerPoint,
            HeightPoints: (height * textAreaShare - verticalInsets) / EmuPerPoint,
            FontSizePoints: size / HundredthsPerPoint);
    }

    private static bool IsEllipse(Shape?[] shapes) =>
        shapes.Select(shape => shape?.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value).FirstOrDefault(found => found is not null)
            == A.ShapeTypeValues.Ellipse;

    private static Shape? FindMasterShape(SlideMasterPart masterPart, bool isTitle) =>
        masterPart.SlideMaster?.CommonSlideData?.ShapeTree?.Elements<Shape>().FirstOrDefault(shape =>
            shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is { } placeholder
            && (isTitle
                ? placeholder.Type?.Value == PlaceholderValues.Title
                : placeholder.Type is null || placeholder.Type.Value == PlaceholderValues.Body));

    private static int? FontSizeOf(Shape? shape) =>
        shape?.TextBody?.ListStyle?.Level1ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>()?.FontSize?.Value;

    private static int? MasterStyleFontSize(SlideMasterPart masterPart, bool isTitle)
    {
        var styles = masterPart.SlideMaster?.TextStyles;
        OpenXmlCompositeElement? style = isTitle ? styles?.TitleStyle : styles?.BodyStyle;
        return style?.GetFirstChild<A.Level1ParagraphProperties>()?.GetFirstChild<A.DefaultRunProperties>()?.FontSize?.Value;
    }

    private static long Inset(Shape?[] shapes, Func<A.BodyProperties, Int32Value?> inset, long fallback) =>
        shapes.Select(shape => shape?.TextBody?.BodyProperties is { } body ? inset(body)?.Value : null)
            .FirstOrDefault(found => found is not null) ?? fallback;
}

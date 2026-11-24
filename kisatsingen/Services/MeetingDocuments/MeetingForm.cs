using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.WordDocuments;

namespace kisatsingen.Services.MeetingDocuments;

// Fills the meeting template's heading and its form fields. Each field follows
// a bold label ("Møte", "Dato", ...) in a paragraph styled Informasjon, and is
// found by that label rather than by position.
internal static class MeetingForm
{
    public const string NoticeHeading = "Møteinnkalling";
    public const string MinutesHeading = "Referat";

    private const string HeadingStyle = "Overskrift1";
    private const string FormStyle = "Informasjon";
    private const string LabelStyle = "Sterk";

    public static void Fill(Body body, MeetingDocumentSpec spec)
    {
        FillHeading(body, spec.Kind == MeetingDocumentKind.Minutes ? MinutesHeading : NoticeHeading);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Møte"] = spec.Meeting,
            ["Dato"] = spec.Date,
            ["Tid"] = spec.Time,
            ["Sted"] = spec.Place,
            ["Innkalt"] = spec.Invited,
            ["Forfall"] = spec.Absent,
            ["Referent"] = spec.MinuteTaker
        };

        var fields = FindFields(body);
        var missing = values.Keys.Where(label => !fields.ContainsKey(label)).ToList();
        if (missing.Count > 0)
        {
            throw TemplateChanged($"no field labelled {string.Join(", ", missing)}");
        }

        foreach (var (label, field) in fields)
        {
            ContentControls.ReplaceWithText(field, values.GetValueOrDefault(label) ?? "");
        }
    }

    private static void FillHeading(Body body, string heading)
    {
        var paragraph = body.Elements<Paragraph>().FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == HeadingStyle)
            ?? throw TemplateChanged("no heading");

        foreach (var child in paragraph.ChildElements.Where(child => child is not ParagraphProperties).ToList())
        {
            child.Remove();
        }

        paragraph.Append(WordText.CreateRun(heading));
    }

    private static Dictionary<string, SdtRun> FindFields(Body body)
    {
        var fields = new Dictionary<string, SdtRun>(StringComparer.Ordinal);
        var formParagraphs = body.Elements<Paragraph>().Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == FormStyle);

        foreach (var field in formParagraphs.SelectMany(paragraph => paragraph.Elements<SdtRun>()))
        {
            var label = field.ElementsBefore().OfType<Run>().LastOrDefault(IsLabel)?.InnerText.Trim();
            if (label is not null)
            {
                fields.TryAdd(label, field);
            }
        }

        return fields;
    }

    private static bool IsLabel(Run run) => run.RunProperties?.RunStyle?.Val?.Value == LabelStyle;

    private static InvalidOperationException TemplateChanged(string what) =>
        new($"The meeting Word template has {what}. The template changed; update {nameof(MeetingForm)} to match it.");
}

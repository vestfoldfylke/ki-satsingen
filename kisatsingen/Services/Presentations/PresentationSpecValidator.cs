using System.Xml;

namespace kisatsingen.Services.Presentations;

// Checks a spec before anything is built, and names every problem at once so
// the model can correct its call in one retry. Empty when the spec is valid.
// The size limits are policy for new calls; the structure is what the builder
// needs, and is all that a stored call is checked against.
internal static class PresentationSpecValidator
{
    public const int MaxSlides = 40;
    public const int MaxPresentationTitleLength = 200;
    public const int MaxSlideTitleLength = 400;
    public const int MaxTextLength = 300;
    public const int MaxBulletLength = 300;
    public const int MaxBulletsPerList = 12;

    private const string TextField = "text";
    private const string BulletsField = "bullets";
    private const string SecondColumnField = "secondColumn";

    public static IReadOnlyList<string> Validate(PresentationSpec spec) => Check(spec, isEnforcingLimits: true);

    public static IReadOnlyList<string> ValidateStructure(PresentationSpec spec) => Check(spec, isEnforcingLimits: false);

    private static List<string> Check(PresentationSpec spec, bool isEnforcingLimits)
    {
        var problems = new List<string>();
        var limits = isEnforcingLimits ? Limits.Enforced : Limits.None;

        CheckText(problems, "Presentasjonens tittel", spec.Title, limits.PresentationTitleLength, isRequired: true);

        if (spec.Slides.Count == 0 || spec.Slides.Count > limits.Slides)
        {
            problems.Add($"Presentasjonen har {spec.Slides.Count} lysbilder; den må ha fra 1 til {MaxSlides}. Del innholdet på færre lysbilder, eller lag flere presentasjoner.");
        }

        if (!Enum.IsDefined(spec.Theme))
        {
            problems.Add($"Fargetemaet {spec.Theme} finnes ikke. Velg ett av: {string.Join(", ", Enum.GetNames<PresentationTheme>())}.");
        }

        for (var i = 0; i < spec.Slides.Count; i++)
        {
            CheckSlide(problems, $"Lysbilde {i + 1}", spec.Slides[i], limits);
        }

        return problems;
    }

    // The model's JSON can hold a null slide although PresentationSpec cannot,
    // so this is checked on the arguments, before a spec is made of them.
    public static IReadOnlyList<string> FindMissingSlides(IReadOnlyList<SlideSpec?> slides) =>
    [
        .. slides
            .Select((slide, index) => (slide, index))
            .Where(entry => entry.slide is null)
            .Select(entry => $"Lysbilde {entry.index + 1} er tomt (null). Gi lysbildet en type og en tittel, eller fjern det.")
    ];

    private static void CheckSlide(List<string> problems, string slideName, SlideSpec slide, Limits limits)
    {
        if (!Enum.IsDefined(slide.Kind))
        {
            problems.Add($"{slideName} har typen {slide.Kind}, som ikke finnes. Velg en av: {string.Join(", ", Enum.GetNames<SlideKind>())}.");
            return;
        }

        var layout = SlideLayouts.For(slide.Kind);
        var allowed = AllowedFields(layout);

        CheckText(problems, $"{slideName}: title", slide.Title, limits.SlideTitleLength, isRequired: true);

        CheckAllowed(problems, slideName, slide.Kind, TextField, slide.Text is not null, layout.TextIndex, allowed);
        CheckAllowed(problems, slideName, slide.Kind, BulletsField, slide.Bullets is not null, layout.BulletsIndex, allowed);
        CheckAllowed(problems, slideName, slide.Kind, SecondColumnField, slide.SecondColumn is not null, layout.SecondColumnIndex, allowed);

        CheckText(problems, $"{slideName}: {TextField}", slide.Text, limits.TextLength, isRequired: false);
        CheckList(problems, $"{slideName}: {BulletsField}", slide.Bullets, limits);
        CheckList(problems, $"{slideName}: {SecondColumnField}", slide.SecondColumn, limits);
    }

    private static void CheckAllowed(List<string> problems, string slideName, SlideKind kind, string field, bool isGiven, uint? index, string allowed)
    {
        if (isGiven && index is null)
        {
            problems.Add($"{slideName} er av typen {kind}, som ikke har feltet {field}. Feltene {kind} tar: {allowed}.");
        }
    }

    private static string AllowedFields(SlideLayout layout)
    {
        string?[] fields =
        [
            "title",
            layout.TextIndex is null ? null : TextField,
            layout.BulletsIndex is null ? null : BulletsField,
            layout.SecondColumnIndex is null ? null : SecondColumnField
        ];
        return string.Join(", ", fields.OfType<string>());
    }

    private static void CheckList(List<string> problems, string name, IReadOnlyList<string>? items, Limits limits)
    {
        if (items is null)
        {
            return;
        }

        if (items.Count == 0 || items.Count > limits.BulletsPerList)
        {
            problems.Add($"{name} har {items.Count} punkter; den må ha fra 1 til {MaxBulletsPerList}. Utelat feltet, eller del punktene på flere lysbilder.");
        }

        for (var i = 0; i < items.Count; i++)
        {
            CheckText(problems, $"{name}, punkt {i + 1}", items[i], limits.BulletLength, isRequired: true);
        }
    }

    private static void CheckText(List<string> problems, string name, string? text, int maxLength, bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (isRequired || text is not null)
            {
                problems.Add($"{name} er tom. Skriv inn tekst, eller utelat feltet der det er valgfritt.");
            }
            return;
        }

        if (text.Length > maxLength)
        {
            problems.Add($"{name} har {text.Length} tegn; maks er {maxLength}. Kort ned teksten, eller del den på flere lysbilder.");
        }

        if (!IsXmlText(text))
        {
            problems.Add($"{name} inneholder kontrolltegn som ikke kan stå i en PowerPoint-fil. Fjern dem.");
        }
    }

    private sealed record Limits(int Slides, int PresentationTitleLength, int SlideTitleLength, int TextLength, int BulletLength, int BulletsPerList)
    {
        public static readonly Limits Enforced = new(MaxSlides, MaxPresentationTitleLength, MaxSlideTitleLength, MaxTextLength, MaxBulletLength, MaxBulletsPerList);

        public static readonly Limits None = new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
    }

    // Line breaks and tabs are allowed by XML but break a paragraph oddly;
    // they are not rejected, the builder keeps them as typed.
    private static bool IsXmlText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (!XmlConvert.IsXmlChar(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}

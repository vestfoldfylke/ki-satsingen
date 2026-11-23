using kisatsingen.Services.GeneratedFiles;

namespace kisatsingen.Services.MeetingDocuments;

// Checks a spec before anything is built, and names every problem at once so
// the model can correct its call in one retry. Empty when the spec is valid.
// The size limits are policy for new calls; the structure is what the builder
// needs, and is all that a stored call is checked against.
internal static class MeetingDocumentSpecValidator
{
    public const int MaxAgendaItems = 40;
    public const int MaxShortTextLength = 200;
    public const int MaxNameListLength = 1000;
    public const int MaxItemLength = 50;
    public const int MaxDescriptionLength = 2000;

    public static IReadOnlyList<string> Validate(MeetingDocumentSpec spec) => Check(spec, Limits.Enforced);

    public static IReadOnlyList<string> ValidateStructure(MeetingDocumentSpec spec) => Check(spec, Limits.None);

    // The model's JSON can hold a null item although MeetingDocumentSpec
    // cannot, so this is checked on the arguments, before a spec is made of them.
    public static IReadOnlyList<string> FindMissingAgendaItems(IReadOnlyList<AgendaItemSpec?> items) =>
    [
        .. items
            .Select((item, index) => (item, index))
            .Where(entry => entry.item is null)
            .Select(entry => $"Sak {entry.index + 1} er tom (null). Gi saken nummer og beskrivelse, eller fjern den.")
    ];

    private static List<string> Check(MeetingDocumentSpec spec, Limits limits)
    {
        var problems = new List<string>();

        if (!Enum.IsDefined(spec.Kind))
        {
            problems.Add($"Typen {spec.Kind} finnes ikke. Velg en av: {string.Join(", ", Enum.GetNames<MeetingDocumentKind>())}.");
        }

        CheckText(problems, "Dokumentets tittel", spec.Title, limits.ShortText, isRequired: true);
        CheckText(problems, "Feltet meeting", spec.Meeting, limits.ShortText, isRequired: true);
        CheckText(problems, "Feltet date", spec.Date, limits.ShortText, isRequired: false);
        CheckText(problems, "Feltet time", spec.Time, limits.ShortText, isRequired: false);
        CheckText(problems, "Feltet place", spec.Place, limits.ShortText, isRequired: false);
        CheckText(problems, "Feltet invited", spec.Invited, limits.NameList, isRequired: false);
        CheckText(problems, "Feltet absent", spec.Absent, limits.NameList, isRequired: false);
        CheckText(problems, "Feltet minuteTaker", spec.MinuteTaker, limits.ShortText, isRequired: false);

        if (spec.AgendaItems.Count == 0 || spec.AgendaItems.Count > limits.AgendaItems)
        {
            problems.Add($"Sakslisten har {spec.AgendaItems.Count} saker; den må ha fra 1 til {MaxAgendaItems}. Del møtet på flere dokumenter, eller slå sammen saker.");
        }

        for (var i = 0; i < spec.AgendaItems.Count; i++)
        {
            CheckAgendaItem(problems, $"Sak {i + 1}", spec.AgendaItems[i], limits);
        }

        return problems;
    }

    private static void CheckAgendaItem(List<string> problems, string name, AgendaItemSpec item, Limits limits)
    {
        CheckText(problems, $"{name}: item", item.Item, limits.Item, isRequired: true);
        CheckText(problems, $"{name}: description", item.Description, limits.Description, isRequired: true);
        CheckText(problems, $"{name}: decision", item.Decision, limits.Description, isRequired: false);
        CheckText(problems, $"{name}: responsible", item.Responsible, limits.ShortText, isRequired: false);
        CheckText(problems, $"{name}: deadline", item.Deadline, limits.ShortText, isRequired: false);
    }

    // An optional field given as blank is refused rather than kept: left out,
    // it stays a blank field in the form anyway.
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
            problems.Add($"{name} har {text.Length} tegn; maks er {maxLength}. Kort ned teksten.");
        }

        if (!XmlText.IsValid(text))
        {
            problems.Add($"{name} inneholder kontrolltegn som ikke kan stå i en Word-fil. Fjern dem.");
        }
    }

    private sealed record Limits(int AgendaItems, int ShortText, int NameList, int Item, int Description)
    {
        public static readonly Limits Enforced = new(MaxAgendaItems, MaxShortTextLength, MaxNameListLength, MaxItemLength, MaxDescriptionLength);

        public static readonly Limits None = new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
    }
}

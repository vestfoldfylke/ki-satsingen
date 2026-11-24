using System.ComponentModel;

namespace kisatsingen.Services.MeetingDocuments;

// One row of the template's agenda table (Saksliste). The description comes
// first since it is the one field required; the table's column order is
// AgendaTable's.
public sealed record AgendaItemSpec(
    [property: Description("What the item is about.")]
    string Description,
    [property: Description("The item's number or case number, e.g. \"1\" or \"12/26\". Leave it out when the text gives none.")]
    string? Item = null,
    [property: Description("What was decided; usually only in minutes. Leave it out when nothing is decided yet.")]
    string? Decision = null,
    [property: Description("Who is responsible for following the item up. Leave it out when not given.")]
    string? Responsible = null,
    [property: Description("The deadline for following the item up, as the user writes it. Leave it out when not given.")]
    string? Deadline = null);

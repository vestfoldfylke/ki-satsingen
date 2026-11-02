using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

// Reasons the save path refuses a file that conversion could not have
// caught, shown to the user as is. The token cap and invalid characters reuse
// ConversionRejections, so one rule has one wording wherever it is enforced.
internal static class KnowledgeFileSaveRejections
{
    public const string DuplicateInChat = "Filen er allerede lagt ved i denne samtalen.";

    public const string DuplicateInAssistant = "Filen er allerede lagt til i denne assistenten.";

    public static readonly string AssistantFull =
        $"Assistenten har allerede {Assistant.MaxKnowledgeFiles} filer, som er det meste den kan ha. Slett en fil for å legge til en ny.";
}

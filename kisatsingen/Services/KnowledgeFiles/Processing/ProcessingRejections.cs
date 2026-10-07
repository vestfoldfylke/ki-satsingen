namespace kisatsingen.Services.KnowledgeFiles.Processing;

// Reasons processing refuses a file for something other than its content,
// shown to the user as is.
internal static class ProcessingRejections
{
    // Unreachable while uploads and converters share one list of types; kept
    // so a gap between them shows as a message, not a crash.
    public const string UnsupportedType = "Filtypen kan ikke behandles.";

    public const string TimedOut =
        "Behandlingen av filen tok for lang tid og ble stoppet. Prøv igjen, eller del filen opp.";

    public const string ShuttingDown =
        "Behandlingen ble avbrutt fordi tjenesten startet på nytt. Legg ved filen igjen.";
}

namespace kisatsingen.Services.Chat;

// What the attachment line says, and the reasons the turn gives a file it
// could not keep.
internal static class AttachmentTexts
{
    public const string Heading = "Vedlagte filer:";

    public const string ReadBeforeAnswering =
        "Les en vedlagt fil med get_outline eller read_file før du svarer om innholdet.";

    public const string NotSavedReason = "Filen kunne ikke lagres. Legg den ved på nytt.";

    public const string InterruptedReason = "Behandlingen ble avbrutt. Legg den ved på nytt.";

    // fileId is the tools' argument name, so the model copies it straight into a call.
    public static string AvailableEntry(string fileName, Guid fileId) => $"- {fileName} (fileId: {fileId})";

    public static string UnavailableEntry(string fileName, string reason) => $"- {fileName}: ikke tilgjengelig. {reason}";
}

namespace kisatsingen.Services.Attachments;

// Every reason an upload can be refused, shown to the user as is. Written in
// advance and never built from an exception, so no internals reach the page.
internal static class UploadRejections
{
    public const string Empty = "Filen er tom.";

    public const string WrongSignature =
        "Innholdet i filen stemmer ikke med filtypen. Sjekk at filen ikke har fått feil filendelse.";

    public const string Interrupted =
        "Opplastingen ble avbrutt. Prøv igjen.";

    public const string AlreadyPending = "Denne filen er allerede valgt.";

    public const string ConcurrentUploads =
        "Du laster allerede opp filer i en annen fane. Vent til de er ferdige, og prøv igjen.";

    public static string UnsupportedType =>
        $"Filtypen støttes ikke. Du kan legge ved {AttachmentContentTypes.AllowedKindsWithExtensionsText}.";

    public static string TooLarge(long maxBytes) =>
        $"Filen er større enn grensen på {ByteSize.Format(maxBytes)}.";

    public static string TooManyPending(int max) =>
        $"Du har allerede {max} vedlegg som venter. Send meldingen eller fjern noen vedlegg først.";

    public static string TooManyPendingBytes(long maxBytes) =>
        $"Vedleggene som venter er til sammen større enn {ByteSize.Format(maxBytes)}. Send meldingen eller fjern noen vedlegg først.";

    public static string TooManyInSelection(int max) =>
        $"Du kan velge opptil {max} filer om gangen.";
}

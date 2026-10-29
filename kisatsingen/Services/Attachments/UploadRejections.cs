namespace kisatsingen.Services.Attachments;

// Key is a metric label value: written out beside the message, so rewording
// or renaming never renames a time series. Message is shown to the user as is.
public sealed record UploadRejection(string Key, string Message);

// Every reason an upload can be refused. Written in advance and never built
// from an exception, so no internals reach the page.
internal static class UploadRejections
{
    public static UploadRejection Empty { get; } = new("empty", "Filen er tom.");

    public static UploadRejection WrongFileType { get; } = new(
        "wrong_file_type",
        "Innholdet i filen stemmer ikke med filtypen. Sjekk at filen ikke har fått feil filendelse.");

    public static UploadRejection Interrupted { get; } = new(
        "interrupted",
        "Opplastingen ble avbrutt. Prøv igjen.");

    // The attachment folder is gone or unreadable. Only a restart fixes it.
    public static UploadRejection Unavailable { get; } = new(
        "unavailable",
        "Vi beklager, opplasting av filer virker ikke akkurat nå. Feilen må rettes av en systemadministrator.");

    public static UploadRejection AlreadyPending { get; } = new("already_pending", "Denne filen er allerede valgt.");

    // A browser never declares a negative size; only a tampered one does.
    public static UploadRejection UnreadableSize { get; } = new(
        "unreadable_size",
        "Filstørrelsen kunne ikke leses. Velg filen på nytt.");

    public static UploadRejection InstanceBudgetFull { get; } = new(
        "instance_budget_full",
        "Det lastes opp mye akkurat nå. Prøv igjen om litt.");

    public static UploadRejection UnsupportedType => new(
        "unsupported_type",
        $"Filtypen støttes ikke. Du kan laste opp {AttachmentContentTypes.AllowedKindsWithExtensionsText}.");

    public static UploadRejection TooLarge(long maxBytes) => new(
        "too_large",
        $"Filen er større enn grensen på {ByteSize.Format(maxBytes)}.");

    public static UploadRejection TooManyPending(int max) => new(
        "too_many_pending",
        $"Du har allerede {max} vedlegg som venter. Send meldingen eller fjern noen vedlegg først.");

    public static UploadRejection TooManyPendingBytes(long maxBytes) => new(
        "too_many_pending_bytes",
        $"Vedleggene som venter er til sammen større enn {ByteSize.Format(maxBytes)}. Send meldingen eller fjern noen vedlegg først.");

    public static UploadRejection TooManyInSelection(int max) => new(
        "too_many_in_selection",
        $"Du kan velge opptil {max} filer om gangen.");
}

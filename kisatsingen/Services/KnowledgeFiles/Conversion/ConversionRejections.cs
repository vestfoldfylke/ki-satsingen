using System.Globalization;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Every reason a conversion can refuse a file, shown to the user as is.
// Written in advance and never built from an exception.
internal static class ConversionRejections
{
    // Explicit, because these are built on the processing worker: it serves no
    // request, so the app's request culture never reaches it.
    private static readonly CultureInfo Norwegian = CultureInfo.GetCultureInfo("nb-NO");

    public const string Binary =
        "Filen ser ikke ut til å være tekst. Sjekk at den ikke har fått feil filendelse.";

    public const string InvalidCharacters =
        "Filen har ugyldige tegn. Lagre den som UTF-8 og prøv igjen.";

    public const string Empty = "Filen inneholder ingen tekst.";

    public const string MalformedImage = "Bildet kunne ikke leses. Filen kan være skadet.";

    public static string TooManyTokens(int maxEstimatedTokens) => string.Create(
        Norwegian,
        $"Filen har mer tekst enn grensen på omtrent {maxEstimatedTokens:N0} tokens. Del den opp og legg ved delene hver for seg.");

    public static string ImageTooLarge(long width, long height) =>
        $"Bildet er for stort ({width} x {height} piksler). Last opp en mindre versjon.";
}

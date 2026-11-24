using System.Text.RegularExpressions;

namespace kisatsingen.AIFunctions.WebSearch;

// Norwegian fødselsnummer and D-nummer: 11 digits whose last two are check
// digits computed from the others, so a random 11-digit number rarely passes.
// A D-nummer adds 4 to the first digit and uses the same check digits.
// https://no.wikipedia.org/wiki/F%C3%B8dselsnummer
//
// Checked by the rule for numbers issued from 2032, which accepts more values
// for the first check digit. Every number issued before passes it too, so one
// rule covers both, as Skatteetaten asks of systems that check numbers.
// https://skatteetaten.github.io/folkeregisteret-api-dokumentasjon/nytt-fodselsnummer-fra-2032/
// Built like Skatteetaten's validerKontrollsifferFoedselsEllerDnummer:
// https://skatteetaten.github.io/folkeregisteret-api-dokumentasjon/dokumenter/foedselsEllerDNummerValidator.js
internal static partial class NationalIdentityNumber
{
    private const int CheckDigitModulus = 11;

    private static readonly int[] FirstCheckDigitWeights = [3, 7, 6, 1, 8, 9, 4, 5, 2];
    private static readonly int[] SecondCheckDigitWeights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    // The weighted sum of the digits before a check digit plus the check digit
    // itself, modulo 11. For the first, only 0 before 2032; 0, 1, 2 or 3 from 2032.
    private static readonly int[] AcceptedFirstCheckRemainders = [0, 1, 2, 3];
    private const int AcceptedSecondCheckRemainder = 0;

    // Whole, or with a space or dash after the birth date, and not part of a
    // longer number. [0-9] rather than \d, which also matches other scripts'
    // digits.
    [GeneratedRegex(@"(?<![0-9])[0-9]{6}[\s-]?[0-9]{5}(?![0-9])")]
    private static partial Regex Candidate();

    public static bool IsIn(string text) =>
        Candidate().Matches(text).Any(match => HasValidCheckDigits(string.Concat(match.Value.Where(char.IsAsciiDigit))));

    private static bool HasValidCheckDigits(string number)
    {
        var digits = number.Select(character => character - '0').ToArray();
        var firstCheckRemainder = (WeightedSum(digits, FirstCheckDigitWeights) + digits[9]) % CheckDigitModulus;
        var secondCheckRemainder = (WeightedSum(digits, SecondCheckDigitWeights) + digits[10]) % CheckDigitModulus;
        return AcceptedFirstCheckRemainders.Contains(firstCheckRemainder)
            && secondCheckRemainder == AcceptedSecondCheckRemainder;
    }

    private static int WeightedSum(int[] digits, int[] weights) =>
        digits.Zip(weights, (digit, weight) => digit * weight).Sum();
}

namespace kisatsingen.Services;

// The one rule for turning text length into tokens, shared by the chat's
// context meter and knowledge-file limits, so a file's estimate agrees with
// what the meter shows once the file is read.
//
// A heuristic, not a tokenizer: GPT and Mistral use different vocabularies, so
// a real tokenizer would be exact for one and confidently wrong for the other.
internal static class TokenEstimate
{
    // English runs about four characters per token; Norwegian is denser on both
    // providers (æ, ø, å and split compounds). Deliberately low so the estimate
    // runs high: it drives warnings and limits, and erring early is the
    // recoverable mistake.
    private const int TokenLengthInCharacters = 3;

    public static long FromCharacters(long characters) => characters / TokenLengthInCharacters;
}

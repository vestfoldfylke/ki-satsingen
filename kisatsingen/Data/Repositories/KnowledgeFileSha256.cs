using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

internal static class KnowledgeFileSha256
{
    // Exactly 64 hex chars, normalised to lowercase so the DB representation is
    // canonical regardless of which pipeline produced it — which the unique
    // indexes depend on.
    public static string Normalise(string sha256)
    {
        if (sha256.Length != KnowledgeFile.Sha256HexLength || !IsLowerableHex(sha256))
        {
            // Truncate the offending input in the message so a caller passing a
            // multi-megabyte blob does not push that whole payload into logs.
            const int previewLength = 16;
            var preview = sha256.Length <= previewLength ? sha256 : sha256[..previewLength] + "…";
            throw new ArgumentException(
                $"SHA-256 must be exactly {KnowledgeFile.Sha256HexLength} hexadecimal characters (got {sha256.Length}: '{preview}').",
                nameof(sha256));
        }
        return sha256.ToLowerInvariant();
    }

    private static bool IsLowerableHex(string value)
    {
        foreach (var c in value)
        {
            var isHex = (c is >= '0' and <= '9') || (c is >= 'a' and <= 'f') || (c is >= 'A' and <= 'F');
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }
}

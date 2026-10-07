using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

public abstract record TextDecodeResult
{
    public sealed record Decoded(string Text) : TextDecodeResult;

    public sealed record Rejected(string Reason) : TextDecodeResult;
}

// Bytes to text, for files that carry no declared encoding. The order of the
// checks is the design: each one is only safe because of those before it.
internal static class TextDecoder
{
    // Enough to catch a binary file by its NULs without scanning all of it.
    private const int BinaryProbeLength = 8 * 1024;

    private const int Windows1252CodePage = 1252;

    // By number, not as a literal: a raw one is invisible in source.
    private const char ByteOrderMark = (char)0xFEFF;

    // Strict: .NET has no validity check for UTF-16 bytes, and a lenient
    // decode would turn bad bytes into replacement characters that reach a
    // model as if they were text.
    private static readonly UnicodeEncoding StrictUtf16Le = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    // Taken from the provider directly rather than registered globally, so
    // nothing else in the process starts accepting code pages by accident.
    private static readonly Encoding Windows1252 =
        CodePagesEncodingProvider.Instance.GetEncoding(Windows1252CodePage)
        ?? throw new InvalidOperationException($"Code page {Windows1252CodePage} is not available from CodePagesEncodingProvider, so older Norwegian text files cannot be read.");

    public static TextDecodeResult Decode(ReadOnlySpan<byte> bytes) =>
        DecodeBytes(bytes) switch
        {
            TextDecodeResult.Decoded decoded => Normalize(decoded.Text),
            var rejected => rejected
        };

    private static TextDecodeResult DecodeBytes(ReadOnlySpan<byte> bytes)
    {
        // Byte-order marks first: a UTF-16 file is full of NULs, which the
        // binary check below would otherwise refuse.
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            return DecodeUtf8(bytes[Encoding.UTF8.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            return DecodeUtf16(StrictUtf16Le, bytes[Encoding.Unicode.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return DecodeUtf16(StrictUtf16Be, bytes[Encoding.BigEndianUnicode.Preamble.Length..]);
        }

        if (bytes[..Math.Min(bytes.Length, BinaryProbeLength)].Contains((byte)0))
        {
            return new TextDecodeResult.Rejected(ConversionRejections.Binary);
        }

        if (Utf8.IsValid(bytes))
        {
            return new TextDecodeResult.Decoded(Encoding.UTF8.GetString(bytes));
        }

        // A file with UTF-8 in it is a UTF-8 file with broken bytes, not a
        // 1252 file: read as 1252, every æøå in it would turn into Ã¦Ã¸Ã¥.
        if (ContainsMultiByteUtf8Character(bytes))
        {
            return new TextDecodeResult.Rejected(ConversionRejections.InvalidCharacters);
        }

        // 1252 decodes almost any byte sequence, which is only safe because
        // binary files were refused above.
        return new TextDecodeResult.Decoded(Windows1252.GetString(bytes));
    }

    private static TextDecodeResult DecodeUtf8(ReadOnlySpan<byte> bytes) =>
        Utf8.IsValid(bytes)
            ? new TextDecodeResult.Decoded(Encoding.UTF8.GetString(bytes))
            : new TextDecodeResult.Rejected(ConversionRejections.InvalidCharacters);

    private static TextDecodeResult DecodeUtf16(UnicodeEncoding encoding, ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new TextDecodeResult.Decoded(encoding.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return new TextDecodeResult.Rejected(ConversionRejections.InvalidCharacters);
        }
    }

    private static TextDecodeResult Normalize(string text)
    {
        // Reachable through UTF-16, where NUL is a valid character: still
        // never text, and the database would refuse it later.
        if (text.Contains('\0'))
        {
            return new TextDecodeResult.Rejected(ConversionRejections.Binary);
        }

        var normalized = text.TrimStart(ByteOrderMark).ReplaceLineEndings("\n");
        return string.IsNullOrWhiteSpace(normalized)
            ? new TextDecodeResult.Rejected(ConversionRejections.Empty)
            : new TextDecodeResult.Decoded(normalized);
    }

    // True when at least one valid character in the bytes takes more than one
    // byte in UTF-8: the mark of a UTF-8 file, which 1252 text never has.
    private static bool ContainsMultiByteUtf8Character(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(bytes, out _, out var consumed);
            if (status == OperationStatus.Done && consumed > 1)
            {
                return true;
            }

            bytes = bytes[Math.Max(consumed, 1)..];
        }

        return false;
    }
}

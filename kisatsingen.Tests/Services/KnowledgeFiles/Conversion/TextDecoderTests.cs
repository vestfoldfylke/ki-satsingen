using System.Text;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class TextDecoderTests
{
    private const string Norwegian = "Blåbærsyltetøy på skiva.";

    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private static string DecodedText(byte[] bytes) =>
        Assert.IsType<TextDecodeResult.Decoded>(TextDecoder.Decode(bytes)).Text;

    private static string RejectionOf(byte[] bytes) =>
        Assert.IsType<TextDecodeResult.Rejected>(TextDecoder.Decode(bytes)).Reason;

    [Fact]
    public void Utf8_without_a_byte_order_mark_is_read_as_utf8()
    {
        Assert.Equal(Norwegian, DecodedText(Encoding.UTF8.GetBytes(Norwegian)));
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_not_part_of_the_text()
    {
        Assert.Equal(Norwegian, DecodedText([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Norwegian)]));
    }

    // Notepad's "Unicode": full of NUL bytes, which the binary check must not see first.
    [Fact]
    public void Utf16_little_endian_with_its_byte_order_mark_is_read_as_utf16()
    {
        Assert.Equal(Norwegian, DecodedText([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Norwegian)]));
    }

    [Fact]
    public void Utf16_big_endian_with_its_byte_order_mark_is_read_as_utf16()
    {
        Assert.Equal(Norwegian, DecodedText([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Norwegian)]));
    }

    [Fact]
    public void An_older_windows_1252_file_is_read_with_its_norwegian_letters_intact()
    {
        Assert.Equal(Norwegian, DecodedText(Windows1252.GetBytes(Norwegian)));
    }

    // Guessing 1252 here would quietly turn every æøå into Ã¦Ã¸Ã¥.
    [Fact]
    public void Utf8_with_a_single_broken_byte_is_rejected_rather_than_guessed_as_windows_1252()
    {
        byte[] brokenUtf8 = [.. Encoding.UTF8.GetBytes(Norwegian), 0xFF];

        Assert.Equal(ConversionRejections.InvalidCharacters, RejectionOf(brokenUtf8));
    }

    [Fact]
    public void Nul_bytes_without_a_byte_order_mark_mean_a_binary_file()
    {
        Assert.Equal(ConversionRejections.Binary, RejectionOf([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08]));
    }

    [Fact]
    public void A_nul_character_inside_utf16_text_is_still_refused()
    {
        Assert.Equal(ConversionRejections.Binary, RejectionOf([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("tekst\0mer")]));
    }

    [Fact]
    public void Windows_and_old_mac_line_endings_become_newlines()
    {
        Assert.Equal("en\nto\ntre", DecodedText(Encoding.UTF8.GetBytes("en\r\nto\rtre")));
    }

    [Fact]
    public void A_file_of_nothing_but_whitespace_is_rejected_as_empty()
    {
        Assert.Equal(ConversionRejections.Empty, RejectionOf(Encoding.UTF8.GetBytes(" \n\t\n")));
    }
}

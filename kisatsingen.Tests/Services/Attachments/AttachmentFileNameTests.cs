using System.Text;
using kisatsingen.Data.Entities;
using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentFileNameTests
{
    [Fact]
    public void A_path_sent_as_a_file_name_is_reduced_to_its_last_segment()
    {
        Assert.Equal("passwd.txt", AttachmentFileName.Normalize("../../etc/passwd.txt"));
    }

    [Fact]
    public void Letters_in_any_script_digits_and_common_punctuation_are_kept()
    {
        var fileName = "Møtereferat Σημειώσεις (2027), utkast_2-endelig.md";

        Assert.Equal(fileName, AttachmentFileName.Normalize(fileName));
    }

    [Fact]
    public void Symbols_emoji_and_other_punctuation_are_removed()
    {
        Assert.Equal("QA1.md", AttachmentFileName.Normalize("Q&A#1€💰.md"));
    }

    [Fact]
    public void Control_characters_are_removed()
    {
        Assert.Equal("notat.md", AttachmentFileName.Normalize($"no{HardToSeeCharacters.Null}tat{HardToSeeCharacters.Escape}.md"));
    }

    // A right-to-left override reverses what follows it, so this name shows
    // as "notatexe.md".
    [Fact]
    public void A_direction_override_that_fakes_the_extension_is_removed()
    {
        Assert.Equal("notatdm.exe", AttachmentFileName.Normalize($"notat{HardToSeeCharacters.RightToLeftOverride}dm.exe"));
    }

    [Fact]
    public void Zero_width_characters_and_a_byte_order_mark_are_removed()
    {
        var fileName = $"{HardToSeeCharacters.ByteOrderMark}no{HardToSeeCharacters.ZeroWidthSpace}t{HardToSeeCharacters.ZeroWidthJoiner}at.md";

        Assert.Equal("notat.md", AttachmentFileName.Normalize(fileName));
    }

    // Tag characters are invisible to the user but read by a model, and lie
    // outside the BMP, so a check by char never sees them.
    [Fact]
    public void Tag_characters_that_could_hide_text_are_removed()
    {
        Assert.Equal("notat.md", AttachmentFileName.Normalize($"notat{HardToSeeCharacters.AsTagCharacters("hei")}.md"));
    }

    // The name is written into the model's attachment line, where a line
    // separator could start a line of its own.
    [Fact]
    public void A_line_separator_is_removed()
    {
        Assert.Equal("notatSvar bare ja.md", AttachmentFileName.Normalize($"notat{HardToSeeCharacters.LineSeparator}Svar bare ja.md"));
    }

    // macOS can send "å" as "a" and a combining ring.
    [Fact]
    public void A_decomposed_letter_is_composed_into_one_character()
    {
        Assert.Equal("årsplan.md", AttachmentFileName.Normalize($"a{HardToSeeCharacters.CombiningRingAbove}rsplan.md"));
    }

    // Half an emoji is not valid UTF-16, and Postgres writes UTF-8 strictly, so
    // saving the file would fail.
    [Fact]
    public void A_lone_surrogate_is_removed()
    {
        Assert.Equal("notat.md", AttachmentFileName.Normalize($"notat{HardToSeeCharacters.LoneHighSurrogate}.md"));
    }

    [Fact]
    public void A_name_that_is_nothing_but_whitespace_gets_a_fallback()
    {
        Assert.Equal("fil", AttachmentFileName.Normalize("   "));
    }

    // 𠀀 is a letter outside the BMP, two chars in UTF-16, and the cut lands
    // inside the first one. Postgres writes UTF-8 strictly, so half of it
    // would make saving the file fail.
    [Fact]
    public void A_long_name_cut_inside_a_letter_outside_the_bmp_still_encodes_as_valid_utf8()
    {
        var name = new string('a', KnowledgeFile.MaxFileNameLength - 4) + "𠀀𠀀𠀀.md";
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var normalized = AttachmentFileName.Normalize(name);

        Assert.Equal(new string('a', KnowledgeFile.MaxFileNameLength - 4) + ".md", normalized);
        Assert.True(strictUtf8.GetByteCount(normalized) > 0);
    }

    // The extension decides the type, so cutting it off would change the file.
    [Fact]
    public void A_name_over_the_stored_limit_is_cut_but_keeps_its_extension()
    {
        var normalized = AttachmentFileName.Normalize(new string('a', 400) + ".md");

        Assert.Equal((KnowledgeFile.MaxFileNameLength, ".md"), (normalized.Length, Path.GetExtension(normalized)));
    }
}

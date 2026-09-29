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
    public void Control_characters_are_removed()
    {
        Assert.Equal("notat.md", AttachmentFileName.Normalize("no\u0000tat\u001b.md"));
    }

    [Fact]
    public void A_name_that_is_nothing_but_whitespace_gets_a_fallback()
    {
        Assert.Equal("fil", AttachmentFileName.Normalize("   "));
    }

    // The extension decides the type, so cutting it off would change the file.
    [Fact]
    public void A_name_over_the_stored_limit_is_cut_but_keeps_its_extension()
    {
        var normalized = AttachmentFileName.Normalize(new string('a', 400) + ".md");

        Assert.Equal((KnowledgeFile.MaxFileNameLength, ".md"), (normalized.Length, Path.GetExtension(normalized)));
    }
}

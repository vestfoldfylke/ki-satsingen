using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class AttachmentLineTests
{
    [Fact]
    public void A_turn_without_attachments_has_no_line()
    {
        var line = AttachmentLine.Render([]);

        Assert.Null(line);
    }

    [Fact]
    public void A_file_still_being_processed_is_left_out()
    {
        var line = AttachmentLine.Render([TurnAttachment.Processing("notat.md")]);

        Assert.Null(line);
    }

    [Fact]
    public void Available_files_are_named_with_their_id_and_the_instruction_to_read_them_first()
    {
        var fileId = Guid.NewGuid();

        var line = AttachmentLine.Render([new TurnAttachment("notat.md", fileId)]);

        Assert.Equal($"{AttachmentTexts.Heading}\n{AttachmentTexts.AvailableEntry("notat.md", fileId)}\n{AttachmentTexts.ReadBeforeAnswering}", line);
    }

    [Fact]
    public void Unavailable_files_get_their_reason_and_no_instruction_to_read()
    {
        var line = AttachmentLine.Render([new TurnAttachment("bilde.png", UnavailableReason: "Bildet kunne ikke leses.")]);

        Assert.Equal($"{AttachmentTexts.Heading}\n{AttachmentTexts.UnavailableEntry("bilde.png", "Bildet kunne ikke leses.")}", line);
    }
}

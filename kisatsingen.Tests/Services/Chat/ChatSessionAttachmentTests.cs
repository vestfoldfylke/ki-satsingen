using kisatsingen.Tests.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionAttachmentTests
{
    [Fact]
    public async Task Opening_another_chat_discards_what_was_attached_in_this_one()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Attachments.UploadAsync(new InputFileChangeEventArgs([new FakeBrowserFile("notat.md", UploadBytes.Text)]));

        await harness.Session.LoadAsync(Guid.NewGuid());

        Assert.Empty(harness.Attachments.Pending);
        Assert.Empty(harness.AttachmentEnvironment.FilesOnDisk);
    }
}

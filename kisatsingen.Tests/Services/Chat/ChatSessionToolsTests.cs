using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// Which tools a turn is offered is recorded nowhere else, so these tests are
// its record.
public sealed class ChatSessionToolsTests
{
    [Fact]
    public async Task A_plain_chat_is_offered_the_file_generation_tools()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Subset(
            new HashSet<string> { "create_presentation", "list_presentation_pictures", "create_word_document", "create_meeting_document" },
            harness.Client.LastToolNames.ToHashSet());
    }
}

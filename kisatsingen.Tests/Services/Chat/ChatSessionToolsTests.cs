using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.Data.Entities;
using kisatsingen.Tests.AIFunctions.WebSearch;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// Which tools a turn is offered is recorded nowhere else, so these tests are
// its record.
public sealed class ChatSessionToolsTests
{
    private static readonly HashSet<string> FileGenerationToolNames =
        ["create_presentation", "list_presentation_pictures", "create_word_document", "create_meeting_document"];

    private const string SearchWebToolName = "search_web";

    [Fact]
    public async Task A_plain_chat_is_offered_the_file_generation_tools()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Subset(FileGenerationToolNames, harness.Client.LastToolNames.ToHashSet());
    }

    [Fact]
    public async Task An_assistant_with_file_generation_enabled_is_offered_the_file_generation_tools()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.StartNewAssistantChatAsync(NewAssistant(isFileGenerationEnabled: true), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.Subset(FileGenerationToolNames, harness.Client.LastToolNames.ToHashSet());
    }

    [Fact]
    public async Task An_assistant_with_file_generation_disabled_is_not_offered_the_file_generation_tools()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.StartNewAssistantChatAsync(NewAssistant(), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.Client.LastToolNames.Intersect(FileGenerationToolNames));
    }

    [Fact]
    public async Task An_assistant_with_web_search_enabled_is_offered_search_and_allowed_to_search()
    {
        await using var harness = HarnessWithSearch();
        await harness.Session.StartNewAssistantChatAsync(NewAssistant(isWebSearchEnabled: true), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.Equal(
            (true, true),
            (harness.Client.LastToolNames.Contains(SearchWebToolName), harness.Repository.SingleStoredTurn.IsWebSearchAllowed));
    }

    [Fact]
    public async Task An_assistant_with_web_search_disabled_is_not_offered_search_even_with_the_switch_on()
    {
        await using var harness = HarnessWithSearch();
        await harness.Session.StartNewAssistantChatAsync(NewAssistant(), assistantFiles: []);
        harness.Session.SetWebSearch(isOn: true);

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(SearchWebToolName, harness.Client.LastToolNames);
    }

    private static ChatSessionHarness HarnessWithSearch() =>
        new(new WebSearchModel("OpenAI", "search-under-test", new FakeSearchClient(), new ChatOptions()));

    private static Assistant NewAssistant(bool isFileGenerationEnabled = false, bool isWebSearchEnabled = false) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Name = "Saksbehandler",
        Visibility = AssistantVisibility.Private,
        IsFileGenerationEnabled = isFileGenerationEnabled,
        IsWebSearchEnabled = isWebSearchEnabled
    };
}

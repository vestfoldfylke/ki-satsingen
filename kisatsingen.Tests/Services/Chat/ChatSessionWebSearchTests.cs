using System.Runtime.CompilerServices;
using System.Text.Json;
using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.Data.Entities;
using kisatsingen.Tests.AIFunctions.WebSearch;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// The search spend lands in its own usage row, beside the turn's, so tokens
// and searches are billed to the search model rather than the chat model.
public sealed class ChatSessionWebSearchTests
{
    // Each search: one billed search, then opening the page it found, which is free.
    private readonly FakeSearchClient _searchClient = new()
    {
        Response = new ChatResponse(new AiMessage(ChatRole.Assistant,
            [
                new WebSearchToolCallContent("ws-1") { Queries = ["skoleskyss"] },
                new TextContent("Skyssgrensen er fire kilometer.")
            ]))
        {
            Usage = new UsageDetails { InputTokenCount = 2_000, OutputTokenCount = 400 },
            RawRepresentation = RawSearchResponse.WithActions(
                """{"type":"search","query":"skoleskyss"}""",
                """{"type":"open_page","url":"https://lovdata.no/forskrift"}""")
        }
    };

    private ChatSessionHarness HarnessWithSearch() =>
        new(new WebSearchModel("OpenAI", "search-under-test", _searchClient, new ChatOptions()));

    [Fact]
    public async Task Every_turn_is_offered_search_when_search_is_configured()
    {
        await using var harness = HarnessWithSearch();

        await harness.Session.SendAsync("hei");

        Assert.Contains(SearchWebTool.Name, harness.Client.LastToolNames);
    }

    [Fact]
    public async Task No_turn_is_offered_search_when_search_is_not_configured()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(SearchWebTool.Name, harness.Client.LastToolNames);
    }

    [Fact]
    public async Task A_turn_that_searched_writes_one_web_search_row_with_the_search_model_its_tokens_and_its_searches()
    {
        await using var harness = HarnessWithSearch();
        harness.Client.OnStream = ct => SearchingTwiceThenAnswering(harness, ct);

        await harness.Session.SendAsync("hvor langt må elever bo fra skolen?");

        var searchRow = Assert.Single(harness.UserUsageRepository.Inserts, insert => insert.Kind == UsageKind.WebSearch);
        Assert.Equal(
            ("OpenAI", "search-under-test", 4_000L, 800L, (int?)2, harness.StoredTurn.Id),
            (searchRow.Provider, searchRow.ModelId, searchRow.Usage.InputTokens, searchRow.Usage.OutputTokens, searchRow.WebSearchCount, searchRow.TurnId));
    }

    [Fact]
    public async Task A_turn_that_did_not_search_writes_no_web_search_row()
    {
        await using var harness = HarnessWithSearch();

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(harness.UserUsageRepository.Inserts, insert => insert.Kind == UsageKind.WebSearch);
    }

    // Calls the offered tool the way FunctionInvokingChatClient would.
    private static async IAsyncEnumerable<ChatResponseUpdate> SearchingTwiceThenAnswering(
        ChatSessionHarness harness,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var searchTool = harness.Client.LastOptions?.Tools?.OfType<AIFunction>().Single(tool => tool.Name == SearchWebTool.Name)
            ?? throw new InvalidOperationException("The turn was not offered search.");
        var arguments = new AIFunctionArguments { ["query"] = JsonSerializer.SerializeToElement("skoleskyss") };

        await searchTool.InvokeAsync(arguments, ct);
        await searchTool.InvokeAsync(arguments, ct);

        yield return new ChatResponseUpdate(ChatRole.Assistant, "Fire kilometer.");
    }
}

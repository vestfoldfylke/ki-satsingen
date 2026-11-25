using System.Runtime.CompilerServices;
using System.Text.Json;
using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.AIFunctions.WebSearch;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_turn_is_offered_search_whether_it_is_on_or_off(bool isWebSearchOn)
    {
        await using var harness = HarnessWithSearch();
        harness.Session.SetWebSearch(isOn: isWebSearchOn);

        await harness.Session.SendAsync("hei");

        Assert.Contains(SearchWebTool.Name, harness.Client.LastToolNames);
    }

    [Fact]
    public async Task A_new_chat_starts_with_search_off_and_reaches_no_search_model()
    {
        await using var harness = HarnessWithSearch();
        harness.Client.OnStream = ct => SearchingTwiceThenAnswering(harness, ct);

        await harness.Session.SendAsync("hvor langt må elever bo fra skolen?");

        Assert.Equal(0, _searchClient.CallCount);
    }

    [Fact]
    public async Task Search_cannot_be_turned_on_when_search_is_not_configured()
    {
        await using var harness = new ChatSessionHarness();
        harness.Session.SetWebSearch(isOn: true);

        await harness.Session.SendAsync("hei");

        Assert.Equal((false, false), (harness.Session.IsWebSearchOn, harness.Client.LastToolNames.Contains(SearchWebTool.Name)));
    }

    [Fact]
    public async Task A_turn_sent_with_search_on_records_that_search_was_allowed()
    {
        await using var harness = HarnessWithSearch();
        harness.Session.SetWebSearch(isOn: true);

        await harness.Session.SendAsync("hei");

        Assert.True(harness.Repository.SingleStoredTurn.IsWebSearchAllowed);
    }

    [Fact]
    public async Task Turning_search_on_while_an_answer_streams_applies_from_the_next_message()
    {
        await using var harness = HarnessWithSearch();
        harness.Client.OnStream = ct => TurningSearchOnThenAnswering(harness, ct);

        await harness.Session.SendAsync("hei");

        Assert.Equal(
            (false, true),
            (harness.Repository.SingleStoredTurn.IsWebSearchAllowed, harness.Session.IsWebSearchOn));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reopening_a_chat_resumes_search_as_its_last_turn_was_sent(bool wasLastTurnAllowed)
    {
        await using var harness = HarnessWithSearch();
        var chat = harness.Repository.Store(ChatWithTurns(!wasLastTurnAllowed, wasLastTurnAllowed));

        await harness.Session.OpenChatAsync(chat.Id);

        Assert.Equal(wasLastTurnAllowed, harness.Session.IsWebSearchOn);
    }

    [Fact]
    public async Task Starting_a_new_chat_turns_search_off()
    {
        await using var harness = HarnessWithSearch();
        harness.Session.SetWebSearch(isOn: true);

        await harness.Session.StartNewPlainChatAsync();

        Assert.False(harness.Session.IsWebSearchOn);
    }

    [Fact]
    public async Task A_turn_that_searched_writes_one_web_search_row_with_the_search_model_its_tokens_and_its_searches()
    {
        await using var harness = HarnessWithSearch();
        harness.Session.SetWebSearch(isOn: true);
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
        harness.Session.SetWebSearch(isOn: true);

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

    private static async IAsyncEnumerable<ChatResponseUpdate> TurningSearchOnThenAnswering(
        ChatSessionHarness harness,
        [EnumeratorCancellation] CancellationToken ct)
    {
        harness.Session.SetWebSearch(isOn: true);
        await Task.Yield();

        yield return new ChatResponseUpdate(ChatRole.Assistant, "Hei.");
    }

    private static ChatEntity ChatWithTurns(params bool[] isWebSearchAllowedPerTurn) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Title = "stored",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Turns =
        [
            .. isWebSearchAllowedPerTurn.Select(isWebSearchAllowed => new ChatTurn
            {
                Id = Guid.NewGuid(),
                Prompt = "hei",
                SystemPrompt = "be brief",
                ModelKey = FakeChatModelCatalog.DefaultKey.Value,
                IsWebSearchAllowed = isWebSearchAllowed,
                Status = nameof(TurnStatus.Completed),
                AnswerJson = "[]"
            })
        ]
    };
}

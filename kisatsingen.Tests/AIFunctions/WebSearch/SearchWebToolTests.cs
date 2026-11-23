using System.ClientModel.Primitives;
using System.Text.Json;
using kisatsingen.AIFunctions.WebSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Responses;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.

namespace kisatsingen.Tests.AIFunctions.WebSearch;

// The tool against a scripted search model. The raw responses are built from
// OpenAI's wire JSON, so the billed count is pinned to the action types OpenAI
// documents, not to the SDK's internal classes.
public sealed class SearchWebToolTests
{
    private readonly FakeSearchClient _client = new();
    private readonly WebSearchQuota _quota = new("openai", "search-under-test");
    private readonly WarningCapture _logger = new();

    private AIFunction Tool() =>
        SearchWebTool.Create(
            new WebSearchModel("openai", "search-under-test", _client, new ChatOptions()),
            _quota,
            _logger);

    private async Task<string> SearchAsync(string? query, CancellationToken ct = default)
    {
        var arguments = new AIFunctionArguments { ["query"] = query is null ? null : JsonSerializer.SerializeToElement(query) };
        return Assert.IsType<string>(await Tool().InvokeAsync(arguments, ct));
    }

    [Fact]
    public void The_tool_is_search_web_with_one_required_query()
    {
        var schema = Tool().JsonSchema;

        Assert.Equal(
            ("search_web", "query", "string", "query"),
            (Tool().Name,
                Assert.Single(schema.GetProperty("properties").EnumerateObject()).Name,
                schema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString(),
                Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString()));
    }

    [Fact]
    public void The_description_states_the_search_limit()
    {
        Assert.Contains($"{WebSearchQuota.MaxSearchesPerTurn} searches", Tool().Description);
    }

    // The query leaves for OpenAI and a search engine, and the query's own
    // description is what the model reads while it writes one.
    [Fact]
    public void The_query_description_forbids_personal_data()
    {
        var queryDescription = Tool().JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("description").GetString();

        Assert.Contains("Never include personal data", queryDescription);
    }

    [Fact]
    public void The_tool_description_says_not_to_search_for_a_private_person()
    {
        Assert.Contains("Do not search for a private person", Tool().Description);
    }

    [Fact]
    public async Task The_result_shape_is_pinned()
    {
        _client.Response = Answer(
            Text("Skyssgrensen er fire kilometer.",
                Citation("https://lovdata.no/forskrift", "Forskrift om skoleskyss"),
                Citation("https://www.vestfoldfylke.no/skyss", title: null)),
            SearchCall("skoleskyss grense"));

        var result = await SearchAsync("skoleskyss");

        Assert.Equal(
            $$"""{"queries":["skoleskyss grense"],"sources":[{"url":"https://lovdata.no/forskrift","title":"Forskrift om skoleskyss"},{"url":"https://www.vestfoldfylke.no/skyss"}],"contentNotice":"{{SearchWebTexts.ContentNotice}}","content":"Skyssgrensen er fire kilometer."}""",
            result);
    }

    [Fact]
    public async Task A_source_cited_twice_is_listed_once()
    {
        _client.Response = Answer(Text("Svar.",
            Citation("https://lovdata.no/forskrift", "Forskrift"),
            Citation("https://lovdata.no/forskrift", "Forskrift")));

        var result = await SearchAsync("skoleskyss");

        Assert.Single(JsonElement.Parse(result).GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task A_citation_with_a_relative_link_is_left_out_of_the_sources()
    {
        _client.Response = Answer(Text("Svar.",
            new CitationAnnotation { Url = new Uri("forskrift", UriKind.Relative), Title = "Forskrift" }));

        var result = await SearchAsync("skoleskyss");

        Assert.Empty(JsonElement.Parse(result).GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task The_query_asked_is_shown_when_the_provider_names_none()
    {
        _client.Response = Answer(Text("Svar."));

        var result = await SearchAsync("skoleskyss");

        Assert.Equal(["skoleskyss"], JsonElement.Parse(result).GetProperty("queries").EnumerateArray().Select(query => query.GetString()));
    }

    // Otherwise the query asked would be shown in its place, and a broken
    // mapping would look like a working one.
    [Fact]
    public async Task Searches_whose_queries_cannot_be_read_are_warned_about()
    {
        _client.Response = Answer(Text("Svar."), raw: RawResponse("""{"type":"search"}"""));

        await SearchAsync("skoleskyss");

        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public async Task An_answer_without_searches_is_not_warned_about()
    {
        _client.Response = Answer(Text("Svar."), raw: RawResponse());

        await SearchAsync("skoleskyss");

        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public async Task Only_search_actions_are_counted_as_billed_searches()
    {
        _client.Response = Answer(Text("Svar."), raw: RawResponse(
            """{"type":"search","query":"skoleskyss"}""",
            """{"type":"open_page","url":"https://lovdata.no/forskrift"}""",
            """{"type":"find_in_page","url":"https://lovdata.no/forskrift","pattern":"kilometer"}""",
            """{"type":"search","query":"skyssgrense"}"""));

        await SearchAsync("skoleskyss");

        Assert.Equal(2, _quota.BilledSearches);
    }

    [Fact]
    public async Task A_search_call_with_an_action_type_openai_does_not_document_is_counted_as_billed()
    {
        _client.Response = Answer(Text("Svar."), raw: RawResponse("""{"type":"search_preview","query":"skoleskyss"}"""));

        await SearchAsync("skoleskyss");

        Assert.Equal(1, _quota.BilledSearches);
    }

    [Fact]
    public async Task A_search_call_whose_action_cannot_be_read_is_counted_as_billed()
    {
        _client.Response = Answer(Text("Svar."), raw: RawResponse(actionJsons: [null]));

        await SearchAsync("skoleskyss");

        Assert.Equal(1, _quota.BilledSearches);
    }

    [Fact]
    public async Task Without_the_raw_openai_response_every_search_call_is_counted_as_billed()
    {
        _client.Response = Answer(Text("Svar."), SearchCall("skoleskyss"), SearchCall("skyssgrense"));

        await SearchAsync("skoleskyss");

        Assert.Equal(2, _quota.BilledSearches);
    }

    [Fact]
    public async Task The_tokens_of_every_search_in_the_turn_are_added_together()
    {
        _client.Response = Answer(Text("Svar."), usage: new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 });

        await SearchAsync("skoleskyss");
        await SearchAsync("skyssgrense");

        Assert.Equal((200L, 40L), (_quota.Usage?.InputTokens, _quota.Usage?.OutputTokens));
    }

    [Theory]
    [InlineData("https://lovdata.no/forskrift")]
    [InlineData("www.lovdata.no")]
    [InlineData("lovdata.no/forskrift skoleskyss")]
    [InlineData("evil.example?x=hemmelig")]
    [InlineData("evil.example:8080")]
    [InlineData("ola@evil.example")]
    [InlineData("site:evil.example?x=hemmelig")]
    public async Task A_query_with_a_web_address_is_refused_without_searching(string query)
    {
        var result = await SearchAsync(query);

        Assert.Equal(($$"""{"error":"{{SearchWebTexts.WebAddressRefused}}"}""", 0), (result, _client.CallCount));
    }

    [Theory]
    [InlineData("site:lovdata.no skoleskyss")]
    [InlineData("budsjett 2025/2026")]
    [InlineData("åpningstider kl. 10:30")]
    public async Task A_query_naming_a_site_or_a_year_span_is_searched(string query)
    {
        _client.Response = Answer(Text("Svar."));

        await SearchAsync(query);

        Assert.Equal(1, _client.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task An_empty_query_is_refused_without_searching(string? query)
    {
        var result = await SearchAsync(query);

        Assert.Equal(($$"""{"error":"{{SearchWebTexts.EmptyQuery}}"}""", 0), (result, _client.CallCount));
    }

    [Fact]
    public async Task A_search_over_the_limit_is_refused_without_searching()
    {
        _client.Response = Answer(Text("Svar."));
        for (var search = 0; search < WebSearchQuota.MaxSearchesPerTurn; search++)
        {
            await SearchAsync("skoleskyss");
        }

        var result = await SearchAsync("skoleskyss");

        Assert.Equal(
            ($$"""{"error":"{{SearchWebTexts.LimitReached(WebSearchQuota.MaxSearchesPerTurn)}}"}""", WebSearchQuota.MaxSearchesPerTurn),
            (result, _client.CallCount));
    }

    [Fact]
    public async Task A_failed_search_counts_towards_the_limit()
    {
        _client.Failure = new HttpRequestException("Provider down.");
        for (var search = 0; search < WebSearchQuota.MaxSearchesPerTurn; search++)
        {
            await SearchAsync("skoleskyss");
        }

        var result = await SearchAsync("skoleskyss");

        Assert.Contains(SearchWebTexts.LimitReached(WebSearchQuota.MaxSearchesPerTurn), result);
    }

    [Fact]
    public async Task A_failing_provider_is_answered_as_not_available()
    {
        _client.Failure = new HttpRequestException("Provider down.");

        var result = await SearchAsync("skoleskyss");

        Assert.Equal($$"""{"error":"{{SearchWebTexts.NotAvailable}}"}""", result);
    }

    // What the SDK throws when its own timeout runs out, while the turn goes on.
    [Fact]
    public async Task A_provider_timeout_is_answered_as_not_available()
    {
        _client.Failure = new TaskCanceledException("The request timed out.");

        var result = await SearchAsync("skoleskyss");

        Assert.Equal($$"""{"error":"{{SearchWebTexts.NotAvailable}}"}""", result);
    }

    [Fact]
    public async Task A_search_stopped_by_the_user_is_cancelled_rather_than_answered()
    {
        using var stop = new CancellationTokenSource();
        _client.Failure = new OperationCanceledException(stop.Token);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchAsync("skoleskyss", stop.Token));
    }

    [Fact]
    public async Task A_search_without_text_is_answered_as_no_result()
    {
        _client.Response = Answer(Text("  "));

        var result = await SearchAsync("skoleskyss");

        Assert.Equal($$"""{"error":"{{SearchWebTexts.NoResult}}"}""", result);
    }

    [Fact]
    public void A_factory_without_a_search_model_starts_no_search()
    {
        var factory = new SearchWebToolFactory(model: null, NullLogger<SearchWebToolFactory>.Instance);

        Assert.Null(factory.StartTurn());
    }

    private static TextContent Text(string text, params CitationAnnotation[] citations) =>
        new(text) { Annotations = [.. citations] };

    private static CitationAnnotation Citation(string url, string? title) =>
        new() { Url = new Uri(url), Title = title };

    private static WebSearchToolCallContent SearchCall(string query) =>
        new(callId: $"ws-{query}") { Queries = [query] };

    private static ChatResponse Answer(TextContent text, params WebSearchToolCallContent[] calls) =>
        Answer(text, raw: null, usage: null, calls);

    private static ChatResponse Answer(TextContent text, ResponseResult? raw = null, UsageDetails? usage = null, params WebSearchToolCallContent[] calls) =>
        new(new AiMessage(ChatRole.Assistant, [.. calls, text])) { RawRepresentation = raw, Usage = usage };

    // A null action is a call OpenAI sent without one.
    private static ResponseResult RawResponse(params string?[] actionJsons)
    {
        var output = actionJsons.Select((action, index) =>
            $$"""{"type":"web_search_call","id":"ws_{{index}}","status":"completed"{{(action is null ? "" : $",\"action\":{action}")}}}""");
        var json = $$"""{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"search-under-test","output":[{{string.Join(",", output)}}],"parallel_tool_calls":true,"tool_choice":"auto","tools":[]}""";
        return ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private sealed class WarningCapture : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

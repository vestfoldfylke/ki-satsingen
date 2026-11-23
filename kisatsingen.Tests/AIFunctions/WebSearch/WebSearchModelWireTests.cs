using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using kisatsingen.AIFunctions.WebSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.WebSearch;

// What the adapter sends for the search model's options, and what it reads
// from OpenAI's answer, can change on an upgrade without a compile error, so
// both are pinned in OpenAI's wire format, through the client the search model
// is built with.
public sealed class WebSearchModelWireTests
{
    private const string Query = "skoleskyss";

    [Fact]
    public async Task The_request_offers_only_openais_web_search_tool_weighted_towards_norway()
    {
        using var body = await RequestBodyAsync();

        Assert.Equal(
            """[{"type":"web_search","user_location":{"type":"approximate","country":"NO"},"search_context_size":"medium"}]""",
            body.RootElement.GetProperty("tools").GetRawText());
    }

    [Theory]
    [InlineData("model", "\"gpt-6-luna\"")]
    [InlineData("store", "false")]
    [InlineData("max_tool_calls", "10")]
    [InlineData("tool_choice", "\"required\"")]
    [InlineData("include", """["web_search_call.action.sources"]""")]
    [InlineData("reasoning", """{"effort":"low"}""")]
    public async Task The_request_sends_the_setting_the_search_model_is_built_with(string property, string expectedJson)
    {
        using var body = await RequestBodyAsync();

        Assert.Equal(expectedJson, body.RootElement.GetProperty(property).GetRawText());
    }

    [Fact]
    public async Task An_answer_in_openais_format_becomes_the_tools_result()
    {
        var (result, _) = await SearchAsync(AnswerWithTwoActions);

        Assert.Equal(
            $$"""{"queries":["skoleskyss Vestfold"],"sources":[{"url":"https://lovdata.no/forskrift","title":"Forskrift om skoleskyss"}],"contentNotice":"{{SearchWebTexts.ContentNotice}}","content":"Skyssgrensen er fire kilometer."}""",
            result);
    }

    [Fact]
    public async Task Only_the_search_action_in_openais_format_is_counted_as_billed()
    {
        var (_, quota) = await SearchAsync(AnswerWithTwoActions);

        Assert.Equal(1, quota.BilledSearches);
    }

    [Fact]
    public async Task The_usage_in_openais_format_is_added_to_the_turn()
    {
        var (_, quota) = await SearchAsync(AnswerWithTwoActions);

        var usage = quota.Usage;
        Assert.Equal((5000L, 4096L, 300L, 100L), (usage?.InputTokens, usage?.CachedInputTokens, usage?.OutputTokens, usage?.ReasoningTokens));
    }

    private static async Task<JsonDocument> RequestBodyAsync()
    {
        var handler = new RespondingHandler(AnswerWithTwoActions);
        await SearchAsync(handler);

        return JsonDocument.Parse(Assert.Single(handler.Bodies));
    }

    private static Task<(string Result, WebSearchQuota Quota)> SearchAsync(string answer) =>
        SearchAsync(new RespondingHandler(answer));

    private static async Task<(string Result, WebSearchQuota Quota)> SearchAsync(RespondingHandler handler)
    {
        var model = WebSearchModel.Create(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            new HttpClientPipelineTransport(new HttpClient(handler)));
        var quota = new WebSearchQuota(provider: model.Provider, modelId: model.ModelId);
        var tool = SearchWebTool.Create(model, quota, NullLogger.Instance);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = JsonSerializer.SerializeToElement(Query) });

        return (Assert.IsType<string>(result), quota);
    }

    // A search, then opening the page it found, which OpenAI does not bill, then
    // the report citing that page.
    private const string AnswerWithTwoActions =
        """
        {"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"gpt-6-luna",
         "output":[
          {"type":"web_search_call","id":"ws_1","status":"completed","action":{"type":"search","query":"skoleskyss Vestfold","sources":[{"type":"url","url":"https://lovdata.no/forskrift"}]}},
          {"type":"web_search_call","id":"ws_2","status":"completed","action":{"type":"open_page","url":"https://lovdata.no/forskrift"}},
          {"type":"message","id":"msg_1","status":"completed","role":"assistant","content":[
           {"type":"output_text","text":"Skyssgrensen er fire kilometer.","annotations":[
            {"type":"url_citation","start_index":0,"end_index":31,"url":"https://lovdata.no/forskrift","title":"Forskrift om skoleskyss"}]}]}],
         "usage":{"input_tokens":5000,"input_tokens_details":{"cached_tokens":4096},"output_tokens":300,"output_tokens_details":{"reasoning_tokens":100},"total_tokens":5300}}
        """;

    // Keeps each request and answers it with one scripted response.
    private sealed class RespondingHandler(string answer) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json")
            };
        }
    }
}

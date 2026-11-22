using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// What the adapter sends for our options can change on an upgrade without a
// compile error, so each model's options are pinned against the real streamed
// request body, through the client that model is built with.
public sealed class ChatModelOptionsRequestTests
{
    private const string ModelUnderTest = "model-under-test";

    private const string EmptyStream = "data: [DONE]\n\n";

    private static readonly ChatModel TurnModel = new()
    {
        Key = FakeChatModelCatalog.DefaultKey,
        DisplayName = "Luna",
        ShortDescription = "short",
        LongDescription = "long",
        Provider = "test",
        ModelId = ModelUnderTest,
        ContextWindowTokens = 128_000,
        IconName = "icon"
    };

    private delegate Func<IServiceProvider, IChatClient> ClientFactory(
        string providerCredential,
        Uri endpoint,
        string modelId,
        PipelineTransport? transport);

    private static readonly Dictionary<string, (Func<ChatOptions> Options, ClientFactory Client)> ModelsUnderTest = new()
    {
        ["luna"] = (ChatModelServiceCollectionExtensions.LunaOptions, ChatModelServiceCollectionExtensions.OpenAiResponsesChat),
        ["mistral"] = (ChatModelServiceCollectionExtensions.MistralOptions, ChatModelServiceCollectionExtensions.MistralResponsesChat),
        ["mistral-small"] = (ChatModelServiceCollectionExtensions.MistralSmallOptions, ChatModelServiceCollectionExtensions.MistralResponsesChat)
    };

    // ChatOptions.ModelId overrides the id the client was built with, silently.
    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    public async Task The_model_id_given_to_the_client_reaches_the_provider(string model)
    {
        using var body = await StreamedRequestBodyAsync(model);

        Assert.Equal(ModelUnderTest, body.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    [InlineData("mistral-small")]
    public async Task Requests_send_store_false(string model)
    {
        using var body = await StreamedRequestBodyAsync(model);

        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Theory]
    [InlineData("luna", "medium")]
    [InlineData("mistral", "medium")]
    [InlineData("mistral-small", "high")]
    public async Task Requests_ask_for_the_reasoning_effort_the_model_is_set_to(string model, string effort)
    {
        using var body = await StreamedRequestBodyAsync(model);

        Assert.Equal(effort, body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    // OpenAI returns no reasoning text unless a summary is asked for.
    [Fact]
    public async Task Luna_requests_ask_for_a_reasoning_summary()
    {
        using var body = await StreamedRequestBodyAsync("luna");

        Assert.Equal("detailed", body.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
    }

    // With store=false OpenAI keeps no reasoning items, so a tool round can only
    // carry the reasoning back if it was returned encrypted.
    [Fact]
    public async Task Luna_requests_ask_for_the_reasoning_back_encrypted()
    {
        using var body = await StreamedRequestBodyAsync("luna");

        Assert.Contains(
            "reasoning.encrypted_content",
            body.RootElement.GetProperty("include").EnumerateArray().Select(item => item.GetString()));
    }

    // What the include above is for: without the encrypted content, OpenAI looks
    // the reasoning item up by id, finds nothing stored, and rejects the round.
    // With a summary streamed first, as Luna asks for, the summary and the encrypted
    // content must still go back as one item.
    [Fact]
    public async Task A_Luna_tool_round_sends_the_reasoning_back_as_one_item_with_its_encrypted_content()
    {
        var options = ChatModelServiceCollectionExtensions.LunaOptions();
        options.Tools = [AIFunctionFactory.Create(() => "kl. 12", "probe")];
        var capture = new CapturingHandler(ResponsesStreamSummarisedReasoningThenCallingProbe, EmptyStream);

        await StreamAsync("luna", options, capture);

        using var toolRound = JsonDocument.Parse(capture.Bodies[1]);
        var reasoning = Assert.Single(toolRound.RootElement.GetProperty("input").EnumerateArray(), item => item.GetProperty("type").GetString() == "reasoning");
        Assert.Equal(
            ("rs_1", "encrypted-by-openai"),
            (reasoning.GetProperty("id").GetString(), reasoning.GetProperty("encrypted_content").GetString()));
    }

    // Through the real client, so the SDK update the part index is read from
    // must survive the tool-calling layer to reach the builder.
    [Fact]
    public async Task Each_part_of_a_Luna_reasoning_summary_is_stored_as_a_paragraph_of_its_own()
    {
        var options = ChatModelServiceCollectionExtensions.LunaOptions();
        options.Tools = [AIFunctionFactory.Create(() => "kl. 12", "probe")];
        var capture = new CapturingHandler(ResponsesStreamSummarisedReasoningThenCallingProbe, EmptyStream);
        var builder = new TurnBuilder(Turn.Start("hei", TurnModel, "be brief", []));

        await StreamAsync("luna", options, capture, update => builder.Apply(update));

        Assert.Equal("**Første** del.\n\n**Andre** del.", Assert.Single(builder.Snapshot().Answer.OfType<ReasoningSegment>()).Text);
    }

    // Both summaries are part 0 of their own item, so the index alone would not tell them apart.
    [Fact]
    public async Task The_summaries_of_two_reasoning_items_in_a_row_are_stored_as_paragraphs_of_their_own()
    {
        var capture = new CapturingHandler(ResponsesStreamTwoReasoningItems);
        var builder = new TurnBuilder(Turn.Start("hei", TurnModel, "be brief", []));

        await StreamAsync("luna", ChatModelServiceCollectionExtensions.LunaOptions(), capture, update => builder.Apply(update));

        Assert.Equal("**A** ferdig.\n\n**B** neste.", Assert.Single(builder.Snapshot().Answer.OfType<ReasoningSegment>()).Text);
    }

    // With store=false there is no stored response to point at, and Mistral's
    // stateless API rejects previous_response_id outright.
    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    public async Task A_tool_round_resends_the_whole_history_instead_of_pointing_at_the_previous_response(string model)
    {
        var options = ModelsUnderTest[model].Options();
        options.Tools = [AIFunctionFactory.Create(() => "kl. 12", "probe")];
        var capture = new CapturingHandler(ResponsesStreamCallingProbe, EmptyStream);

        await StreamAsync(model, options, capture);

        using var toolRound = JsonDocument.Parse(capture.Bodies[1]);
        Assert.False(toolRound.RootElement.TryGetProperty("previous_response_id", out _));
        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            toolRound.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetProperty("type").GetString()));
    }

    private static async Task<JsonDocument> StreamedRequestBodyAsync(string model, ChatOptions? options = null)
    {
        var capture = new CapturingHandler(EmptyStream);

        await StreamAsync(model, options ?? ModelsUnderTest[model].Options(), capture);

        return JsonDocument.Parse(capture.Bodies[0]);
    }

    private static async Task StreamAsync(string model, ChatOptions options, CapturingHandler capture, Action<ChatResponseUpdate>? onUpdate = null)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var createClient = ModelsUnderTest[model].Client(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            ModelUnderTest,
            new HttpClientPipelineTransport(new HttpClient(capture)));
        using var client = createClient(services);

        await foreach (var update in client.GetStreamingResponseAsync("hei", options))
        {
            onUpdate?.Invoke(update);
        }
    }

    // One round that calls "probe", without echoing store, so only the request's
    // own flag can keep the adapter from chaining on the response id.
    private const string ResponsesStreamCallingProbe =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"in_progress"}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":2,"output_index":0,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}}

        event: response.completed
        data: {"type":"response.completed","sequence_number":3,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}]}}

        data: [DONE]


        """;

    // A reasoning item as OpenAI streams it with store=false and a detailed summary:
    // two summary parts, then the item with its encrypted content.
    private const string ResponsesStreamSummarisedReasoningThenCallingProbe =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"reasoning","id":"rs_1","summary":[]}}

        event: response.reasoning_summary_part.added
        data: {"type":"response.reasoning_summary_part.added","sequence_number":2,"item_id":"rs_1","output_index":0,"summary_index":0,"part":{"type":"summary_text","text":""}}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":3,"item_id":"rs_1","output_index":0,"summary_index":0,"delta":"**Første** del."}

        event: response.reasoning_summary_text.done
        data: {"type":"response.reasoning_summary_text.done","sequence_number":4,"item_id":"rs_1","output_index":0,"summary_index":0,"text":"**Første** del."}

        event: response.reasoning_summary_part.done
        data: {"type":"response.reasoning_summary_part.done","sequence_number":5,"item_id":"rs_1","output_index":0,"summary_index":0,"part":{"type":"summary_text","text":"**Første** del."}}

        event: response.reasoning_summary_part.added
        data: {"type":"response.reasoning_summary_part.added","sequence_number":6,"item_id":"rs_1","output_index":0,"summary_index":1,"part":{"type":"summary_text","text":""}}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":7,"item_id":"rs_1","output_index":0,"summary_index":1,"delta":"**Andre** del."}

        event: response.reasoning_summary_text.done
        data: {"type":"response.reasoning_summary_text.done","sequence_number":8,"item_id":"rs_1","output_index":0,"summary_index":1,"text":"**Andre** del."}

        event: response.reasoning_summary_part.done
        data: {"type":"response.reasoning_summary_part.done","sequence_number":9,"item_id":"rs_1","output_index":0,"summary_index":1,"part":{"type":"summary_text","text":"**Andre** del."}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":10,"output_index":0,"item":{"type":"reasoning","id":"rs_1","summary":[{"type":"summary_text","text":"**Første** del."},{"type":"summary_text","text":"**Andre** del."}],"encrypted_content":"encrypted-by-openai"}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","sequence_number":11,"output_index":1,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"in_progress"}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":12,"output_index":1,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}}

        event: response.completed
        data: {"type":"response.completed","sequence_number":13,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[{"type":"reasoning","id":"rs_1","summary":[{"type":"summary_text","text":"**Første** del."},{"type":"summary_text","text":"**Andre** del."}],"encrypted_content":"encrypted-by-openai"},{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}]}}

        data: [DONE]


        """;

    // Two reasoning items back to back, each with a summary of one part.
    private const string ResponsesStreamTwoReasoningItems =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":1,"item_id":"rs_1","output_index":0,"summary_index":0,"delta":"**A** ferdig."}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":2,"item_id":"rs_2","output_index":1,"summary_index":0,"delta":"**B** neste."}

        event: response.completed
        data: {"type":"response.completed","sequence_number":3,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[]}}

        data: [DONE]


        """;

    // Keeps every request and answers each with the next scripted stream: only the bodies are under test.
    private sealed class CapturingHandler(params string[] streams) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(streams[Bodies.Count - 1], Encoding.UTF8, "text/event-stream")
            };
        }
    }
}

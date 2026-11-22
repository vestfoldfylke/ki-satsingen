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
    public async Task Instructions_travel_in_the_instructions_field_and_not_as_a_message(string model)
    {
        var options = ModelsUnderTest[model].Options();
        options.Instructions = "Svar kort.";

        using var body = await StreamedRequestBodyAsync(model, options);

        Assert.Equal("Svar kort.", body.RootElement.GetProperty("instructions").GetString());
        Assert.Empty(SystemMessages(body));
    }

    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    public async Task No_system_prompt_is_sent_without_instructions(string model)
    {
        using var body = await StreamedRequestBodyAsync(model);

        Assert.Empty(SystemMessages(body));
        Assert.False(body.RootElement.TryGetProperty("instructions", out _));
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
    [Fact]
    public async Task A_Luna_tool_round_sends_the_reasoning_back_with_its_encrypted_content()
    {
        var options = ChatModelServiceCollectionExtensions.LunaOptions();
        options.Tools = [AIFunctionFactory.Create(() => "kl. 12", "probe")];
        var capture = new CapturingHandler(ResponsesStreamReasoningThenCallingProbe, EmptyStream);

        await StreamAsync("luna", options, capture);

        using var toolRound = JsonDocument.Parse(capture.Bodies[1]);
        var reasoning = Assert.Single(toolRound.RootElement.GetProperty("input").EnumerateArray(), item => item.GetProperty("type").GetString() == "reasoning");
        Assert.Equal("encrypted-by-openai", reasoning.GetProperty("encrypted_content").GetString());
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

    private static IEnumerable<JsonElement> SystemMessages(JsonDocument body) =>
        body.RootElement.GetProperty("input").EnumerateArray()
            .Where(message => message.TryGetProperty("role", out var role) && role.GetString() is "system" or "developer");

    private static async Task<JsonDocument> StreamedRequestBodyAsync(string model, ChatOptions? options = null)
    {
        var capture = new CapturingHandler(EmptyStream);

        await StreamAsync(model, options ?? ModelsUnderTest[model].Options(), capture);

        return JsonDocument.Parse(capture.Bodies[0]);
    }

    private static async Task StreamAsync(string model, ChatOptions options, CapturingHandler capture)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var createClient = ModelsUnderTest[model].Client(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            ModelUnderTest,
            new HttpClientPipelineTransport(new HttpClient(capture)));
        using var client = createClient(services);

        await foreach (var _ in client.GetStreamingResponseAsync("hei", options))
        {
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

    // A reasoning item as OpenAI returns it with store=false: encrypted, with an empty summary.
    private const string ResponsesStreamReasoningThenCallingProbe =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"reasoning","id":"rs_1","summary":[]}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":2,"output_index":0,"item":{"type":"reasoning","id":"rs_1","summary":[],"encrypted_content":"encrypted-by-openai"}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","sequence_number":3,"output_index":1,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"in_progress"}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":4,"output_index":1,"item":{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}}

        event: response.completed
        data: {"type":"response.completed","sequence_number":5,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[{"type":"reasoning","id":"rs_1","summary":[],"encrypted_content":"encrypted-by-openai"},{"type":"function_call","id":"fc_1","call_id":"call-1","name":"probe","arguments":"{}","status":"completed"}]}}

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

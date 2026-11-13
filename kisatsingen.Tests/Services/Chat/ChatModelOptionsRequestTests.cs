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
// request body, through the API that model is on: Luna on Chat Completions,
// Mistral on Responses.
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
        ["luna"] = (ChatModelServiceCollectionExtensions.LunaOptions, ChatModelServiceCollectionExtensions.OpenAiCompletionsChat),
        ["mistral"] = (ChatModelServiceCollectionExtensions.MistralOptions, ChatModelServiceCollectionExtensions.MistralResponsesChat)
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

    [Fact]
    public async Task Luna_instructions_become_exactly_one_system_message()
    {
        var options = ChatModelServiceCollectionExtensions.LunaOptions();
        options.Instructions = "Svar kort.";

        using var body = await StreamedRequestBodyAsync("luna", options);

        var systemMessage = Assert.Single(SystemMessages(body));
        Assert.Equal("Svar kort.", systemMessage.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Mistral_instructions_travel_in_the_instructions_field_and_not_as_a_message()
    {
        var options = ChatModelServiceCollectionExtensions.MistralOptions();
        options.Instructions = "Svar kort.";

        using var body = await StreamedRequestBodyAsync("mistral", options);

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
    public async Task Requests_send_store_false(string model)
    {
        using var body = await StreamedRequestBodyAsync(model);

        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Mistral_requests_ask_for_low_reasoning_effort()
    {
        using var body = await StreamedRequestBodyAsync("mistral");

        Assert.Equal("low", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    // Mistral's Responses API is stateless and rejects previous_response_id.
    [Fact]
    public async Task A_Mistral_tool_round_resends_the_whole_history_instead_of_pointing_at_the_previous_response()
    {
        var options = ChatModelServiceCollectionExtensions.MistralOptions();
        options.Tools = [AIFunctionFactory.Create(() => "kl. 12", "probe")];
        var capture = new CapturingHandler(ResponsesStreamCallingProbe, EmptyStream);

        await StreamAsync("mistral", options, capture);

        using var toolRound = JsonDocument.Parse(capture.Bodies[1]);
        Assert.False(toolRound.RootElement.TryGetProperty("previous_response_id", out _));
        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            toolRound.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetProperty("type").GetString()));
    }

    private static IEnumerable<JsonElement> SystemMessages(JsonDocument body)
    {
        var messages = body.RootElement.TryGetProperty("messages", out var chatMessages)
            ? chatMessages
            : body.RootElement.GetProperty("input");

        return messages.EnumerateArray()
            .Where(message => message.TryGetProperty("role", out var role) && role.GetString() is "system" or "developer");
    }

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

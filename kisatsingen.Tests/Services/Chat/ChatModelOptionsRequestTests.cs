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
// request body. A move to the Responses API changes these bodies too, and should
// fail here so the behaviour is checked again.
public sealed class ChatModelOptionsRequestTests
{
    private const string ModelUnderTest = "model-under-test";

    private static readonly Dictionary<string, Func<ChatOptions>> OptionsByModel = new()
    {
        ["luna"] = ChatModelServiceCollectionExtensions.LunaOptions,
        ["mistral"] = ChatModelServiceCollectionExtensions.MistralOptions
    };

    // ChatOptions.ModelId overrides the id the client was built with, silently.
    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    public async Task The_model_id_given_to_the_client_reaches_the_provider(string model)
    {
        using var body = await StreamedRequestBodyAsync(OptionsByModel[model]());

        Assert.Equal(ModelUnderTest, body.RootElement.GetProperty("model").GetString());
    }

    // Not run per model: the test sets Instructions itself, so only the adapter's
    // mapping is under test.
    [Fact]
    public async Task Instructions_become_exactly_one_system_message()
    {
        var options = ChatModelServiceCollectionExtensions.LunaOptions();
        options.Instructions = "Svar kort.";

        using var body = await StreamedRequestBodyAsync(options);

        var systemMessage = Assert.Single(SystemMessages(body));
        Assert.Equal("Svar kort.", systemMessage.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("luna")]
    [InlineData("mistral")]
    public async Task No_system_message_is_sent_without_instructions(string model)
    {
        using var body = await StreamedRequestBodyAsync(OptionsByModel[model]());

        Assert.Empty(SystemMessages(body));
    }

    [Fact]
    public async Task Luna_requests_send_store_false()
    {
        using var body = await StreamedRequestBodyAsync(ChatModelServiceCollectionExtensions.LunaOptions());

        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Mistral_requests_leave_the_OpenAI_store_flag_out()
    {
        using var body = await StreamedRequestBodyAsync(ChatModelServiceCollectionExtensions.MistralOptions());

        Assert.False(body.RootElement.TryGetProperty("store", out _));
    }

    private static IEnumerable<JsonElement> SystemMessages(JsonDocument body) =>
        body.RootElement.GetProperty("messages").EnumerateArray()
            .Where(message => message.GetProperty("role").GetString() == "system");

    private static async Task<JsonDocument> StreamedRequestBodyAsync(ChatOptions chatOptions)
    {
        var capture = new CapturingHandler();
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var createClient = ChatModelServiceCollectionExtensions.OpenAiCompatibleChat(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            ModelUnderTest,
            new HttpClientPipelineTransport(new HttpClient(capture)));
        using var client = createClient(services);

        await foreach (var _ in client.GetStreamingResponseAsync("hei", chatOptions))
        {
        }

        return JsonDocument.Parse(capture.Body!);
    }

    // Keeps the request and answers with an empty stream: only the body is under test.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
}

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using OpenAI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// TurnBuilder stores a tool result as the text it assumes the OpenAI adapter sent.
// Pinned against both adapters' real request bodies, Chat Completions and
// Responses, so an upgrade that changes either rule fails here instead of
// replaying results the model never saw.
public sealed class ToolResultReplayTests
{
    public static TheoryData<object> Results => new()
    {
        "2026-09-28T12:00:00Z",
        "Error: Function failed.",
        JsonSerializer.SerializeToElement("a string held as JSON"),
        JsonSerializer.SerializeToElement(new { hour = 12, chunks = new[] { "æøå" } })
    };

    [Theory]
    [MemberData(nameof(Results))]
    public async Task A_replayed_result_reaches_Chat_Completions_exactly_as_the_original_did(object result)
    {
        var original = await ChatCompletionsWireContentAsync(new FunctionResultContent("call-1", result));

        var replayed = await ChatCompletionsWireContentAsync(Replay(result));

        Assert.Equal(original, replayed);
    }

    [Theory]
    [MemberData(nameof(Results))]
    public async Task A_replayed_result_reaches_Responses_exactly_as_the_original_did(object result)
    {
        var original = await ResponsesWireContentAsync(new FunctionResultContent("call-1", result));

        var replayed = await ResponsesWireContentAsync(Replay(result));

        Assert.Equal(original, replayed);
    }

    // Through the real path: the builder stores it, the request rebuilds it.
    private static FunctionResultContent Replay(object result)
    {
        var builder = new TurnBuilder(Turn.Start("hei", ModelUnderTest, "be brief", []));
        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe")]));
        builder.Apply(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", result)]));
        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant, "svar"));

        return TranscriptRequest.Build([builder.Snapshot()])
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single();
    }

    private static async Task<string> ChatCompletionsWireContentAsync(FunctionResultContent result)
    {
        var capture = new CapturingHandler(ChatCompletion);
        using var client = OpenAiClient(capture)
            .GetChatClient("model-under-test")
            .AsIChatClient();

        await client.GetResponseAsync(MessagesEndingOn(result));

        using var body = JsonDocument.Parse(capture.Body!);
        return body.RootElement.GetProperty("messages")[2].GetProperty("content").GetRawText();
    }

#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    private static async Task<string> ResponsesWireContentAsync(FunctionResultContent result)
    {
        var capture = new CapturingHandler(ResponsesResult);
        using var client = OpenAiClient(capture)
            .GetResponsesClient()
            .AsIChatClient("model-under-test");

        await client.GetResponseAsync(MessagesEndingOn(result));

        using var body = JsonDocument.Parse(capture.Body!);
        return body.RootElement.GetProperty("input")[2].GetProperty("output").GetRawText();
    }
#pragma warning restore OPENAI001

    private static OpenAIClient OpenAiClient(CapturingHandler capture) =>
        new(
            new ApiKeyCredential("not-used"),
            new OpenAIClientOptions
            {
                Endpoint = new Uri("https://provider.invalid/v1"),
                Transport = new HttpClientPipelineTransport(new HttpClient(capture))
            });

    private static AiMessage[] MessagesEndingOn(FunctionResultContent result) =>
    [
        new(ChatRole.User, "hei"),
        new(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe")]),
        new(ChatRole.Tool, [result])
    ];

    private static readonly ChatModel ModelUnderTest = new()
    {
        Key = FakeChatModelCatalog.DefaultKey,
        DisplayName = "Fast",
        ShortDescription = "short",
        LongDescription = "long",
        Provider = "test",
        ModelId = "wire-id",
        ContextWindowTokens = 128_000,
        IconName = "icon"
    };

    private const string ChatCompletion =
        """{"id":"x","object":"chat.completion","created":0,"model":"model-under-test","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

    private const string ResponsesResult =
        """{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[]}""";

    // Keeps the request and answers with a minimal result: only the body is under test.
    private sealed class CapturingHandler(string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}

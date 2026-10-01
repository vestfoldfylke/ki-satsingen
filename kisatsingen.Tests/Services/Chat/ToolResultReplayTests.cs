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
// Pinned against the adapter's real request body, so an upgrade that changes the
// rule fails here instead of replaying results the model never saw.
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
    public async Task A_replayed_result_reaches_the_provider_exactly_as_the_original_did(object result)
    {
        var original = await WireContentOfResultAsync(new FunctionResultContent("call-1", result));

        var replayed = await WireContentOfResultAsync(Replay(result));

        Assert.Equal(original, replayed);
    }

    // Through the real path: the builder stores it, the request rebuilds it.
    private static FunctionResultContent Replay(object result)
    {
        var builder = new TurnBuilder(Turn.Start("hei", ModelUnderTest, "be brief"));
        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe")]));
        builder.Apply(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", result)]));
        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant, "svar"));

        return TranscriptRequest.Build([builder.Snapshot()])
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single();
    }

    private static async Task<string> WireContentOfResultAsync(FunctionResultContent result)
    {
        var capture = new CapturingHandler();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://provider.invalid/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(capture))
        };
        using var client = new OpenAIClient(new ApiKeyCredential("not-used"), options)
            .GetChatClient("model-under-test")
            .AsIChatClient();

        AiMessage[] messages =
        [
            new(ChatRole.User, "hei"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe")]),
            new(ChatRole.Tool, [result])
        ];
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync(messages));

        using var body = JsonDocument.Parse(capture.Body!);
        return body.RootElement.GetProperty("messages")[2].GetProperty("content").GetRawText();
    }

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

    // Keeps the request and refuses to send it: only the body is under test.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            throw new HttpRequestException("Captured; not sent.");
        }
    }
}

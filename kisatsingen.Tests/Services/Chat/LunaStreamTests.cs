using System.ClientModel.Primitives;
using System.Text;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// What the adapter reads from a stream can change on an upgrade without a compile
// error, so what a turn keeps is pinned against streams in OpenAI's wire format,
// through the client Luna is built with.
public sealed class LunaStreamTests
{
    private const string ModelUnderTest = "model-under-test";

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

    // Usage is what the statistics and every user's cost are built from, and a
    // change in how it is read would store nothing rather than fail.
    [Fact]
    public async Task A_turn_keeps_the_input_cached_output_and_reasoning_tokens_OpenAI_reports()
    {
        var usage = (await TurnFromStreamAsync(StreamReportingUsage)).Metadata?.Usage;

        Assert.Equal(
            (1200L, 1024L, 300L, 200L),
            (usage?.InputTokens, usage?.CachedInputTokens, usage?.OutputTokens, usage?.ReasoningTokens));
    }

    // Through the real client, so the SDK update the part index is read from
    // must survive the tool-calling layer to reach the builder.
    [Fact]
    public async Task Each_part_of_a_reasoning_summary_is_kept_as_a_paragraph_of_its_own()
    {
        var turn = await TurnFromStreamAsync(StreamWithTwoSummaryParts);

        Assert.Equal("**Første** del.\n\n**Andre** del.", Assert.Single(turn.Answer.OfType<ReasoningSegment>()).Text);
    }

    // Both summaries are part 0 of their own item, so the index alone would not tell them apart.
    [Fact]
    public async Task The_summaries_of_two_reasoning_items_in_a_row_are_kept_as_paragraphs_of_their_own()
    {
        var turn = await TurnFromStreamAsync(StreamWithTwoReasoningItems);

        Assert.Equal("**A** ferdig.\n\n**B** neste.", Assert.Single(turn.Answer.OfType<ReasoningSegment>()).Text);
    }

    private static async Task<Turn> TurnFromStreamAsync(string stream)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var client = ChatModelServiceCollectionExtensions.OpenAiResponsesChat(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            ModelUnderTest,
            new HttpClientPipelineTransport(new HttpClient(new StreamingHandler(stream))))(services);
        var builder = new TurnBuilder(Turn.Start("hei", TurnModel, "be brief", []));

        await foreach (var update in client.GetStreamingResponseAsync("hei", ChatModelServiceCollectionExtensions.LunaOptions()))
        {
            builder.Apply(update);
        }

        // Set by TurnStreamer in the app; a turn without it has no metadata to hold the usage.
        builder.DurationMs = 1;
        return builder.Finish(TurnStatus.Completed);
    }

    private const string StreamReportingUsage =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","sequence_number":1,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"Hei."}

        event: response.completed
        data: {"type":"response.completed","sequence_number":2,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[],"usage":{"input_tokens":1200,"input_tokens_details":{"cached_tokens":1024},"output_tokens":300,"output_tokens_details":{"reasoning_tokens":200},"total_tokens":1500}}}

        data: [DONE]


        """;

    private const string StreamWithTwoSummaryParts =
        """
        event: response.created
        data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":0,"status":"in_progress","model":"model-under-test","output":[]}}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":1,"item_id":"rs_1","output_index":0,"summary_index":0,"delta":"**Første** del."}

        event: response.reasoning_summary_text.delta
        data: {"type":"response.reasoning_summary_text.delta","sequence_number":2,"item_id":"rs_1","output_index":0,"summary_index":1,"delta":"**Andre** del."}

        event: response.completed
        data: {"type":"response.completed","sequence_number":3,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"model-under-test","output":[]}}

        data: [DONE]


        """;

    private const string StreamWithTwoReasoningItems =
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

    private sealed class StreamingHandler(string stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
            });
    }
}

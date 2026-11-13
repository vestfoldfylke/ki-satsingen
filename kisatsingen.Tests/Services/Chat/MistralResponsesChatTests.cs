using System.ClientModel.Primitives;
using System.Text;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// A tool round from Mistral Large 4 as captured on 2026-11-13, every line verbatim;
// only most of the reasoning deltas are left out.
public sealed class MistralResponsesChatTests
{
    private const string MistralToolRound =
        """
        event: response.created
        data: {"response":{"id":"resp_1839896c700746678bc27d5d9d29a4ba","access_programs":null,"created_at":1794562332.0,"error":null,"incomplete_details":null,"instructions":null,"metadata":{},"model":"mistral-large-4","object":"response","output":[],"parallel_tool_calls":true,"temperature":null,"tool_choice":"auto","tools":[{"name":"get_time","parameters":{"type":"object","properties":{}},"strict":null,"type":"function","allowed_callers":null,"defer_loading":null,"description":"Returnerer nåværende tid","output_schema":null}],"top_p":null,"background":false,"completed_at":null,"conversation":null,"max_output_tokens":null,"max_tool_calls":null,"moderation":null,"previous_response_id":null,"prompt":null,"prompt_cache_diagnostics":null,"prompt_cache_key":null,"prompt_cache_options":null,"prompt_cache_retention":null,"reasoning":{"context":null,"effort":"low","generate_summary":null,"mode":null,"summary":null},"safety_identifier":null,"service_tier":null,"status":"in_progress","text":{"format":{"type":"text"},"verbosity":null},"tool_usage":null,"top_logprobs":null,"truncation":"disabled","usage":null,"user":null,"store":false},"sequence_number":0,"type":"response.created"}

        event: response.output_item.added
        data: {"item":{"id":"rs_4f19db7baf534830a93869e434cc05ad","summary":[],"type":"reasoning","content":[],"encrypted_content":null,"status":"in_progress"},"output_index":0,"sequence_number":2,"type":"response.output_item.added"}

        event: response.content_part.added
        data: {"content_index":0,"item_id":"rs_4f19db7baf534830a93869e434cc05ad","output_index":0,"part":{"text":"","type":"reasoning_text"},"sequence_number":3,"type":"response.content_part.added"}

        event: response.reasoning_text.delta
        data: {"content_index":0,"delta":"B","item_id":"rs_4f19db7baf534830a93869e434cc05ad","output_index":0,"sequence_number":4,"type":"response.reasoning_text.delta"}

        event: response.reasoning_text.delta
        data: {"content_index":0,"delta":"rukeren sp","item_id":"rs_4f19db7baf534830a93869e434cc05ad","output_index":0,"sequence_number":5,"type":"response.reasoning_text.delta"}

        event: response.reasoning_text.delta
        data: {"content_index":0,"delta":"ør hva","item_id":"rs_4f19db7baf534830a93869e434cc05ad","output_index":0,"sequence_number":6,"type":"response.reasoning_text.delta"}

        event: response.output_item.added
        data: {"item":{"arguments":"","call_id":"3iUsZ23IH","name":"get_time","type":"function_call","id":"fc_77d0a479fd7b4fb99468edfec2cbe073","status":"in_progress"},"output_index":1,"sequence_number":22,"type":"response.output_item.added"}

        event: response.function_call_arguments.delta
        data: {"delta":"{}","item_id":"fc_77d0a479fd7b4fb99468edfec2cbe073","output_index":1,"sequence_number":23,"type":"response.function_call_arguments.delta"}

        event: response.reasoning_text.done
        data: {"content_index":0,"item_id":"rs_4f19db7baf534830a93869e434cc05ad","output_index":0,"sequence_number":24,"text":"Brukeren spør hva klokka er nå på norsk. Jeg bør bruke get_time funksjonen for å hente nåværende tid.","type":"response.reasoning_text.done"}

        event: response.output_item.done
        data: {"item":{"id":"rs_4f19db7baf534830a93869e434cc05ad","summary":[],"type":"reasoning","content":[{"text":"Brukeren spør hva klokka er nå på norsk. Jeg bør bruke get_time funksjonen for å hente nåværende tid.","type":"reasoning_text"}],"encrypted_content":null,"status":"completed"},"output_index":0,"sequence_number":26,"type":"response.output_item.done"}

        event: response.function_call_arguments.done
        data: {"arguments":"{}","item_id":"fc_77d0a479fd7b4fb99468edfec2cbe073","output_index":1,"sequence_number":27,"type":"response.function_call_arguments.done"}

        event: response.output_item.done
        data: {"item":{"arguments":"{}","call_id":"3iUsZ23IH","name":"get_time","type":"function_call","id":"fc_77d0a479fd7b4fb99468edfec2cbe073","status":"completed"},"output_index":1,"sequence_number":28,"type":"response.output_item.done"}

        event: response.completed
        data: {"response":{"id":"resp_1839896c700746678bc27d5d9d29a4ba","access_programs":null,"created_at":1794562332.0,"error":null,"incomplete_details":null,"instructions":null,"metadata":{},"model":"mistral-large-4","object":"response","output":[{"id":"rs_4f19db7baf534830a93869e434cc05ad","summary":[],"type":"reasoning","content":[{"text":"Brukeren spør hva klokka er nå på norsk. Jeg bør bruke get_time funksjonen for å hente nåværende tid.","type":"reasoning_text"}],"encrypted_content":null,"status":"completed"},{"arguments":"{}","call_id":"3iUsZ23IH","name":"get_time","type":"function_call","id":"fc_77d0a479fd7b4fb99468edfec2cbe073","status":"completed"}],"parallel_tool_calls":true,"temperature":null,"tool_choice":"auto","tools":[{"name":"get_time","parameters":{"type":"object","properties":{}},"strict":null,"type":"function","allowed_callers":null,"defer_loading":null,"description":"Returnerer nåværende tid","output_schema":null}],"top_p":null,"background":false,"completed_at":1794562332.0,"conversation":null,"max_output_tokens":null,"max_tool_calls":null,"moderation":null,"previous_response_id":null,"prompt":null,"prompt_cache_diagnostics":null,"prompt_cache_key":null,"prompt_cache_options":null,"prompt_cache_retention":null,"reasoning":{"context":null,"effort":"low","generate_summary":null,"mode":null,"summary":null},"safety_identifier":null,"service_tier":null,"status":"completed","text":{"format":{"type":"text"},"verbosity":null},"tool_usage":null,"top_logprobs":null,"truncation":"disabled","usage":{"input_tokens":69,"input_tokens_details":{"cache_write_tokens":0,"cached_tokens":0},"output_tokens":39,"output_tokens_details":{"reasoning_tokens":31},"total_tokens":108},"user":null,"store":false},"sequence_number":29,"type":"response.completed"}


        """;

    // The round after the tool result; empty, since only the first round is under test.
    private const string EmptyStream = "data: [DONE]\n\n";

    // Mistral's non-streamed answer after the same tool round, as captured on
    // 2026-11-13. The capture was cut off after "text"; the rest is taken from the
    // streamed response above, which is the same object.
    private const string MistralAnswer =
        """
        {"id":"resp_0190bfb31f5c4ebaadf0e481468b016f","access_programs":null,"created_at":1794562487.0,"error":null,"incomplete_details":null,"instructions":null,"metadata":{},"model":"mistral-large-4","object":"response","output":[{"id":"rs_974a56b01a354f749552d92c3f69d28d","summary":[],"type":"reasoning","content":[{"text":"Funksjonen returnerte \"12:00\". Jeg skal svare på norsk siden brukeren spurte på norsk.","type":"reasoning_text"}],"encrypted_content":null,"status":"completed"},{"id":"msg_406e5c8970b940aeb1faf0d69a31ad91","content":[{"annotations":[],"text":"Klokka er nå **12:00**.","type":"output_text","logprobs":null}],"role":"assistant","status":"completed","type":"message","phase":null}],"parallel_tool_calls":true,"temperature":null,"tool_choice":"auto","tools":[{"name":"get_time","parameters":{"type":"object","properties":{}},"strict":null,"type":"function","allowed_callers":null,"defer_loading":null,"description":"Returnerer nåværende tid","output_schema":null}],"top_p":null,"background":false,"completed_at":1794562488.0,"conversation":null,"max_output_tokens":null,"max_tool_calls":null,"moderation":null,"previous_response_id":null,"prompt":null,"prompt_cache_diagnostics":null,"prompt_cache_key":null,"prompt_cache_options":null,"prompt_cache_retention":null,"reasoning":{"context":null,"effort":"low","generate_summary":null,"mode":null,"summary":null},"safety_identifier":null,"service_tier":null,"status":"completed","text":{"format":{"type":"text"},"verbosity":null},"tool_usage":null,"top_logprobs":null,"truncation":"disabled","usage":{"input_tokens":69,"input_tokens_details":{"cache_write_tokens":0,"cached_tokens":0},"output_tokens":39,"output_tokens_details":{"reasoning_tokens":31},"total_tokens":108},"user":null,"store":false}
        """;

    [Fact]
    public async Task A_Mistral_stream_with_a_decimal_created_at_is_read_through_to_its_tool_call()
    {
        var (updates, _) = await StreamAsync(MistralToolRound);

        var call = Assert.Single(Contents<FunctionCallContent>(updates));
        Assert.Equal(("3iUsZ23IH", "get_time"), (call.CallId, call.Name));
    }

    [Fact]
    public async Task Mistral_reasoning_arrives_apart_from_the_answer_as_reasoning_content()
    {
        var (updates, _) = await StreamAsync(MistralToolRound);

        Assert.Equal("Brukeren spør hva", string.Concat(Contents<TextReasoningContent>(updates).Select(reasoning => reasoning.Text)));
    }

    [Fact]
    public async Task One_warning_is_logged_once_Mistral_sends_created_at_as_a_whole_number()
    {
        var (_, warnings) = await StreamAsync(MistralToolRound.Replace("1794562332.0", "1794562332"));

        Assert.Single(warnings);
    }

    [Fact]
    public async Task No_warning_is_logged_for_a_whole_number_created_at_beside_the_responses_own_decimal_one()
    {
        var wholeNumberOnATool = MistralToolRound.Replace("\"output_schema\":null}", "\"output_schema\":null,\"created_at\":1794562332}");

        var (_, warnings) = await StreamAsync(wholeNumberOnATool);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task No_warning_is_logged_for_a_whole_number_created_at_on_an_output_item()
    {
        var wholeNumberOnAnItem = MistralToolRound.Replace("\"encrypted_content\":null,\"status\":\"in_progress\"}", "\"encrypted_content\":null,\"status\":\"in_progress\",\"created_at\":1794562332}");

        var (_, warnings) = await StreamAsync(wholeNumberOnAnItem);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_non_streamed_Mistral_answer_with_a_decimal_created_at_is_read_through_to_its_text()
    {
        await using var services = Services(new WarningCapture());
        using var client = MistralClient(services, new ScriptedAnswers((MistralAnswer, "application/json")));

        var response = await client.GetResponseAsync("Hva er klokka nå?", ChatModelServiceCollectionExtensions.MistralOptions());

        Assert.Equal("Klokka er nå **12:00**.", response.Text);
    }

    private static IEnumerable<T> Contents<T>(IEnumerable<ChatResponseUpdate> updates) =>
        updates.SelectMany(update => update.Contents).OfType<T>();

    private static async Task<(List<ChatResponseUpdate> Updates, List<string> Warnings)> StreamAsync(string firstRound)
    {
        var logger = new WarningCapture();
        await using var services = Services(logger);
        using var client = MistralClient(services, new ScriptedAnswers((firstRound, EventStream), (EmptyStream, EventStream)));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync("Hva er klokka nå?", ChatModelServiceCollectionExtensions.MistralOptions()))
        {
            updates.Add(update);
        }

        return (updates, logger.Warnings);
    }

    private const string EventStream = "text/event-stream";

    private static ServiceProvider Services(WarningCapture logger) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILogger<MistralCreatedAtPolicy>>(logger)
            .BuildServiceProvider();

    private static IChatClient MistralClient(IServiceProvider services, ScriptedAnswers answers) =>
        ChatModelServiceCollectionExtensions.MistralResponsesChat(
            "not-used",
            new Uri("https://provider.invalid/v1"),
            "mistral-large-4",
            new HttpClientPipelineTransport(new HttpClient(answers)))(services);

    private sealed class ScriptedAnswers(params (string Body, string MediaType)[] answers) : HttpMessageHandler
    {
        private int _sent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (body, mediaType) = answers[_sent++];
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
        }
    }

    private sealed class WarningCapture : ILogger<MistralCreatedAtPolicy>
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

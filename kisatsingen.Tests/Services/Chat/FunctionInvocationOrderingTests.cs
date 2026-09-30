using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// Pins the behaviour of Microsoft.Extensions.AI's FunctionInvokingChatClient that
// the live tool view depends on. If an upgrade changes it, these fail before users
// see a tool call appear only once it has already finished.
public sealed class FunctionInvocationOrderingTests
{
    private const string ToolName = "probe";

    // The two orderings that matter: the call reaches us before the tool runs,
    // so it can show as running, and its result before the next round starts.
    [Fact]
    public async Task A_tool_turn_reaches_the_caller_in_the_order_it_happened()
    {
        var log = new List<string>();
        using var client = BuildClient();

        await foreach (var update in client.GetStreamingResponseAsync([new AiMessage(ChatRole.User, "hei")], Options(log)))
        {
            Record(log, update);
        }

        Assert.Equal(["saw text", "saw call", "tool ran", "saw result", "saw text"], log);
    }

    [Fact]
    public async Task A_failing_tool_is_reported_as_a_result_carrying_the_exception()
    {
        var results = new List<FunctionResultContent>();
        using var client = BuildClient();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(string () => throw new InvalidOperationException("tool broke"), ToolName)]
        };

        await foreach (var update in client.GetStreamingResponseAsync([new AiMessage(ChatRole.User, "hei")], options))
        {
            results.AddRange(update.Contents.OfType<FunctionResultContent>());
        }

        Assert.IsType<InvalidOperationException>(Assert.Single(results).Exception);
    }

    // TurnBuilder credits usage to the round still open when it arrives, so a
    // round's usage must reach us before the tool result that closes the round.
    [Fact]
    public async Task A_rounds_usage_reaches_the_caller_before_the_tool_result_that_closes_it()
    {
        var log = new List<string>();
        using var client = BuildClient(new ScriptedClient(reportsUsage: true));

        await foreach (var update in client.GetStreamingResponseAsync([new AiMessage(ChatRole.User, "hei")], Options(log)))
        {
            Record(log, update);
        }

        Assert.Equal(["saw text", "saw call", "saw usage", "tool ran", "saw result", "saw text", "saw usage"], log);
    }

    // TurnBuilder takes the first result as the round's end, so a stop after it
    // counts the next request as sent: only true if no tool is still running.
    [Fact]
    public async Task A_rounds_tool_results_reach_the_caller_only_once_every_tool_has_run()
    {
        var log = new List<string>();
        using var client = BuildClient(new ScriptedClient(callCount: 2));

        await foreach (var update in client.GetStreamingResponseAsync([new AiMessage(ChatRole.User, "hei")], Options(log)))
        {
            Record(log, update);
        }

        Assert.Equal(["saw text", "saw call", "saw call", "tool ran", "tool ran", "saw result", "saw result", "saw text"], log);
    }

    private static IChatClient BuildClient() => BuildClient(new ScriptedClient());

    private static IChatClient BuildClient(ScriptedClient scripted) =>
        scripted.AsBuilder().UseFunctionInvocation().Build();

    private static ChatOptions Options(List<string> log) => new()
    {
        Tools = [AIFunctionFactory.Create(() => { log.Add("tool ran"); return "ok"; }, ToolName)]
    };

    private static void Record(List<string> log, ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 }:
                    log.Add("saw text");
                    break;
                case FunctionCallContent:
                    log.Add("saw call");
                    break;
                case FunctionResultContent:
                    log.Add("saw result");
                    break;
                case UsageContent:
                    log.Add("saw usage");
                    break;
            }
        }
    }

    // Round one talks and calls the tool; round two, which sees the result, answers.
    // Usage, when reported, closes each round the way OpenAI streams it.
    private sealed class ScriptedClient(int callCount = 1, bool reportsUsage = false) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var hasResult = messages.Any(message => message.Contents.OfType<FunctionResultContent>().Any());
            await Task.Yield();

            if (hasResult)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Ferdig");
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Sjekker");

                for (var call = 1; call <= callCount; call++)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent($"call-{call}", ToolName)]);
                }
            }

            if (reportsUsage)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 })]);
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<AiMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

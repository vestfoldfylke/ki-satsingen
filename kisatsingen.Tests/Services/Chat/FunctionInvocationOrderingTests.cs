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

    [Fact]
    public async Task The_call_reaches_the_caller_before_the_tool_runs()
    {
        var log = new List<string>();
        using var client = BuildClient();

        await foreach (var update in client.GetStreamingResponseAsync([new AiMessage(ChatRole.User, "hei")], Options(log)))
        {
            Record(log, update);
        }

        Assert.True(log.IndexOf("saw call") < log.IndexOf("tool ran"), string.Join(", ", log));
    }

    [Fact]
    public async Task The_result_reaches_the_caller_before_the_next_round_starts()
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

    private static IChatClient BuildClient() =>
        new ScriptedClient().AsBuilder().UseFunctionInvocation().Build();

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
            }
        }
    }

    // Round one talks and calls the tool; round two, which sees the result, answers.
    private sealed class ScriptedClient : IChatClient
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
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "Sjekker");
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", ToolName)]);
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

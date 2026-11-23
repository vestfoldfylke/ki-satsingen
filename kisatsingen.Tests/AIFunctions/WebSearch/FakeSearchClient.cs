using Microsoft.Extensions.AI;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.AIFunctions.WebSearch;

// The search model, answering every search with one scripted response.
internal sealed class FakeSearchClient : IChatClient
{
    public ChatResponse Response { get; set; } = new();

    public Exception? Failure { get; set; }

    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        CallCount++;
        return Failure is not null ? Task.FromException<ChatResponse>(Failure) : Task.FromResult(Response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AiMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException("The search tool never streams.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

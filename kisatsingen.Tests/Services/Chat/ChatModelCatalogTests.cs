using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatModelCatalogTests
{
    private static readonly ChatModelKey First = new("first");
    private static readonly ChatModelKey Second = new("second");

    [Fact]
    public void Models_are_listed_in_the_order_they_were_registered()
    {
        using var catalog = Build([Definition(First), Definition(Second)], defaultKey: First);

        Assert.Equal([First, Second], catalog.Models.Select(model => model.Key));
    }

    [Fact]
    public void A_catalogue_with_no_registered_models_refuses_to_build()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Build([], defaultKey: First));

        Assert.Contains("At least one provider", failure.Message, StringComparison.Ordinal);
    }

    // The case that motivates resolving the catalogue at startup: the default
    // model's credentials are absent, so it was dropped, and every chat would
    // otherwise fail on its first turn instead of at boot.
    [Fact]
    public void A_default_that_was_not_registered_refuses_to_build()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Build([Definition(Second)], defaultKey: First));

        Assert.Contains("first", failure.Message, StringComparison.Ordinal);
    }

    // Keys are persisted with every message, so a duplicate would make stored rows
    // ambiguous about which model produced them.
    [Fact]
    public void Two_models_sharing_a_key_refuse_to_build()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Build([Definition(First), Definition(First)], defaultKey: First));

        Assert.Contains("must be unique", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolving_an_unregistered_key_throws_rather_than_falling_back()
    {
        using var catalog = Build([Definition(First)], defaultKey: First);

        Assert.Throws<InvalidOperationException>(() => catalog.Resolve(Second));
    }

    [Fact]
    public void TryGet_reports_an_unregistered_key_as_missing()
    {
        using var catalog = Build([Definition(First)], defaultKey: First);

        Assert.False(catalog.TryGet(Second, out _));
    }

    // Every turn writes its own instructions onto the options it is given, so two
    // turns must never be handed the same instance.
    [Fact]
    public void Each_call_for_options_returns_a_fresh_instance()
    {
        using var catalog = Build([Definition(First)], defaultKey: First);
        var runtime = catalog.Resolve(First);

        var forOneTurn = runtime.CreateOptions();
        var forAnother = runtime.CreateOptions();
        forOneTurn.Instructions = "only this turn's";

        Assert.NotSame(forOneTurn, forAnother);
        Assert.Null(forAnother.Instructions);
    }

    [Fact]
    public void Disposing_the_catalogue_disposes_the_clients_it_built()
    {
        var client = new DisposalRecordingChatClient();
        var catalog = Build([Definition(First, client)], defaultKey: First);

        catalog.Dispose();

        Assert.True(client.IsDisposed);
    }

    private static ChatModelCatalog Build(IReadOnlyList<ChatModelDefinition> definitions, ChatModelKey defaultKey) =>
        new(definitions, defaultKey, EmptyServiceProvider.Instance);

    private static ChatModelDefinition Definition(ChatModelKey key, IChatClient? client = null) =>
        new(
            new ChatModel
            {
                Key = key,
                DisplayName = key.Value,
                ShortDescription = "short",
                LongDescription = "long",
                Provider = "test",
                ModelId = $"{key}-wire-id",
                ContextWindowTokens = 1_000,
                IconName = "icon"
            },
            _ => client ?? new DisposalRecordingChatClient(),
            new ChatOptions());

    // The catalogue passes the provider straight to each factory and never reads
    // it itself, so these tests have nothing to put in one.
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class DisposalRecordingChatClient : IChatClient
    {
        public bool IsDisposed { get; private set; }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<AiMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => IsDisposed = true;
    }
}

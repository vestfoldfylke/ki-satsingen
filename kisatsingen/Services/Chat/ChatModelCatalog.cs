using System.Diagnostics.CodeAnalysis;

namespace kisatsingen.Services.Chat;

// Owns the clients it builds, so it disposes them. Not keyed singletons: that
// would put an ambient IChatClient back in the container, which is exactly what
// this type exists to remove.
internal sealed class ChatModelCatalog : IChatModelCatalog, IDisposable
{
    private readonly Dictionary<ChatModelKey, ChatModelRuntime> _byKey;
    private readonly ChatModel[] _models;

    public ChatModelCatalog(
        IReadOnlyList<ChatModelDefinition> definitions,
        ChatModelKey defaultKey,
        IServiceProvider services)
    {
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                "No chat models could be registered. At least one provider's credentials must be configured; see AddChatModels for which configuration keys are read.");
        }

        _byKey = new Dictionary<ChatModelKey, ChatModelRuntime>(definitions.Count);
        _models = new ChatModel[definitions.Count];

        // A constructor that throws is never disposed, so the clients built so far
        // are disposed here instead of leaking.
        try
        {
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var runtime = new ChatModelRuntime(
                    definition.Model,
                    definition.CreateClient(services),
                    definition.Options);

                if (!_byKey.TryAdd(definition.Model.Key, runtime))
                {
                    // Not in _byKey, so the catch below won't reach it.
                    runtime.Client.Dispose();
                    throw new InvalidOperationException(
                        $"Two chat models are registered under the key '{definition.Model.Key}'. Keys are persisted with every message, so they must be unique; give one of them a different key in AddChatModels.");
                }

                // Registration order is picker order.
                _models[index] = definition.Model;
            }

            if (!TryGet(defaultKey, out var defaultModel))
            {
                throw new InvalidOperationException(
                    $"The default chat model '{defaultKey}' is not among the registered models ({string.Join(", ", _models.Select(model => model.Key))}). Either its credentials are missing from configuration or the default key in AddChatModels is wrong.");
            }

            Default = defaultModel;
        }
        catch
        {
            foreach (var runtime in _byKey.Values)
            {
                // Swallowed so the original exception is the one that surfaces.
                try { runtime.Client.Dispose(); } catch { }
            }
            throw;
        }
    }

    public ChatModel Default { get; }

    public IReadOnlyList<ChatModel> Models => _models;

    public bool TryGet(ChatModelKey key, [MaybeNullWhen(false)] out ChatModel model)
    {
        if (_byKey.TryGetValue(key, out var runtime))
        {
            model = runtime.Model;
            return true;
        }

        model = null;
        return false;
    }

    public ChatModelRuntime Resolve(ChatModelKey key) =>
        _byKey.TryGetValue(key, out var runtime)
            ? runtime
            : throw new InvalidOperationException(
                $"No chat model is registered under the key '{key}'. Check the key against Models before resolving it — a key read back from a client or from storage may name a model that has since been removed.");

    public void Dispose()
    {
        foreach (var runtime in _byKey.Values)
        {
            runtime.Client.Dispose();
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace kisatsingen.Services.Chat;

// Builds every model's client once at startup and hands them out by key.
//
// Owns the clients it builds, so it disposes them: IChatClient is IDisposable
// and the OpenAI one holds a request pipeline. The alternative — registering
// each client as a keyed singleton so the container handled disposal — was
// rejected because it would put an IServiceProvider inside the catalogue for the
// sake of one Dispose, and an ambient IChatClient in the container is exactly
// what this type exists to remove.
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

        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            var runtime = new ChatModelRuntime(
                definition.Model,
                definition.CreateClient(services),
                definition.Options);

            if (!_byKey.TryAdd(definition.Model.Key, runtime))
            {
                throw new InvalidOperationException(
                    $"Two chat models are registered under the key '{definition.Model.Key}'. Keys are persisted with every message, so they must be unique; give one of them a different key in AddChatModels.");
            }

            // Registration order, which is the order the picker shows.
            _models[index] = definition.Model;
        }

        if (!TryGet(defaultKey, out var defaultModel))
        {
            throw new InvalidOperationException(
                $"The default chat model '{defaultKey}' is not among the registered models ({string.Join(", ", _models.Select(model => model.Key))}). Either its credentials are missing from configuration or the default key in AddChatModels is wrong.");
        }

        Default = defaultModel;
    }

    public ChatModel Default { get; }

    // The user is ignored for now — every signed-in user may use every model. See
    // IChatModelCatalog for why the parameter is here anyway.
    public IReadOnlyList<ChatModel> ModelsFor(ClaimsPrincipal user) => _models;

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
                $"No chat model is registered under the key '{key}'. Check the key against ModelsFor before resolving it — a key read back from a client or from storage may name a model that has since been removed.");

    public void Dispose()
    {
        foreach (var runtime in _byKey.Values)
        {
            runtime.Client.Dispose();
        }
    }
}

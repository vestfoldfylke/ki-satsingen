using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// CreateClient is a factory rather than (endpoint, key, model id), so a provider
// that isn't OpenAI-compatible brings its own construction and the catalogue
// never learns what a provider is.
internal sealed record ChatModelDefinition(
    ChatModel Model,
    Func<IServiceProvider, IChatClient> CreateClient,
    ChatOptions Options);

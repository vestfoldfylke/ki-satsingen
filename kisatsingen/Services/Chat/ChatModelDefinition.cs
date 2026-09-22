using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// One model as declared at startup, before its client exists.
//
// CreateClient is a factory rather than provider configuration on purpose. A
// definition that carried (endpoint, apiKey, modelId) would hard-code the OpenAI
// adapter into the catalogue, and a provider that is not OpenAI-compatible —
// Anthropic, which has no first-party Microsoft.Extensions.AI adapter — could
// not be added without rewriting it. As a factory, each provider brings its own
// construction and the catalogue never learns what a provider is.
internal sealed record ChatModelDefinition(
    ChatModel Model,
    Func<IServiceProvider, IChatClient> CreateClient,
    ChatOptions Options);

using System.ClientModel;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace kisatsingen.Services.Chat;

// Models are declared in code so adding or repointing one is a reviewed change.
// Configuration holds only what can't live in the repository: credentials.
internal static class ChatModelServiceCollectionExtensions
{
    private const string OpenAiConfigurationPath = "OpenAI:ApiKey";
    private const string MistralConfigurationPath = "Mistral:ApiKey";

    // Stated although it is the SDK default, so every provider declares where it
    // points. Verified against OpenAI 2.12.0: the request URI is the same set or
    // unset, and there is no OPENAI_BASE_URL override for this to shadow.
    private static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");

    // OpenAI-compatible, so it needs no adapter of its own.
    private static readonly Uri MistralEndpoint = new("https://api.mistral.ai/v1");

    private static readonly ChatModelKey DefaultModelKey = ChatModelKeys.Mistral;

    private static readonly ChatModel MistralModel = new()
    {
        Key = ChatModelKeys.Mistral,
        DisplayName = "Mistral Large",
        ShortDescription = "Raske svar, europeisk leverandør.",
        LongDescription =
            "Kjører hos en europeisk leverandør, så arbeidet blir i EU. "
            + "Rask på oppsummering, omskriving og korte forklaringer.",
        Provider = "Mistral AI",
        ModelId = "mistral-large-latest",
        ContextWindowTokens = 256_000,
        IconName = "placeholder-eu"
    };

    private static readonly ChatModel OpenAIModel = new()
    {
        Key = ChatModelKeys.OpenAI,
        DisplayName = "GPT-6 Luna",
        ShortDescription = "Til de vanskeligere oppgavene.",
        LongDescription =
            "Takler større oppgaver som må løses i flere steg, og holder styr på lange samtaler.",
        Provider = "OpenAI",
        ModelId = "gpt-6-luna",
        ContextWindowTokens = 1_000_000,
        IconName = "placeholder-code"
    };

    private static readonly ChatModel TestModel = new()
    {
        Key = ChatModelKeys.Testing,
        DisplayName = "Test",
        ShortDescription = "Bare for testing, billig drit",
        LongDescription =
            "Kan alt og ingenting på en gang.",
        Provider = "OpenAI",
        ModelId = "gpt-4o-mini",
        ContextWindowTokens = 128_000,
        IconName = "placeholder-dust"
    };

    public static IServiceCollection AddChatModels(this IServiceCollection services)
    {
        // Built in the factory so its warnings reach the real logging pipeline.
        // Program.cs resolves it at startup, so a broken default refuses to boot.
        services.AddSingleton<IChatModelCatalog>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<ChatModelCatalog>>();

            return new ChatModelCatalog(
                BuildDefinitions(configuration, logger),
                DefaultModelKey,
                serviceProvider);
        });

        return services;
    }

    private static IReadOnlyList<ChatModelDefinition> BuildDefinitions(IConfiguration configuration, ILogger logger)
    {
        var definitions = new List<ChatModelDefinition>();

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            MistralModel,
            MistralConfigurationPath,
            providerCredential => (
                OpenAiCompatibleChat(providerCredential, MistralEndpoint, MistralModel.ModelId),
                WithTools()));

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            OpenAIModel,
            OpenAiConfigurationPath,
            providerCredential => (
                OpenAiCompatibleChat(providerCredential, OpenAiEndpoint, OpenAIModel.ModelId),
                WithToolsNoChatCompletionsStoreNoReasoning()));

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            TestModel,
            OpenAiConfigurationPath,
            providerCredential => (
                OpenAiCompatibleChat(providerCredential, OpenAiEndpoint, TestModel.ModelId),
                WithToolsNoChatCompletionsStore()));

        return definitions;
    }

    // Missing credentials drop that provider's models rather than the whole app.
    // The default is the exception: the catalogue refuses to build without it.
    private static void AddIfConfigured(
        List<ChatModelDefinition> definitions,
        IConfiguration configuration,
        ILogger logger,
        ChatModel model,
        string configurationPath,
        Func<string, (Func<IServiceProvider, IChatClient> Client, ChatOptions Options)> build)
    {
        var providerCredential = configuration[configurationPath];
        if (string.IsNullOrWhiteSpace(providerCredential))
        {
            logger.LogWarning(
                "Chat model '{ModelKey}' ({DisplayName}) is unavailable: configuration '{ConfigurationPath}' is missing or empty. Set it to offer this model in the picker.",
                model.Key,
                model.DisplayName,
                configurationPath);
            return;
        }

        var (client, options) = build(providerCredential);
        definitions.Add(new ChatModelDefinition(model, client, options));
    }

    // The only way in, so no model reaches the catalogue without its tools.
    // No `store` here: it is an OpenAI extension, not ours to send elsewhere.
    private static ChatOptions WithTools() => new() { Tools = [.. ChatTools.All] };

    // store=false is already the default, but set so a default change can't start
    // retaining conversations. RawRepresentationFactory because the adapter drops
    // AdditionalProperties.
    private static ChatOptions WithToolsNoChatCompletionsStore() =>
        new()
        {
            Tools = [.. ChatTools.All],
            RawRepresentationFactory = _ => new ChatCompletionOptions { StoredOutputEnabled = false },
        };

    // For gpt-6-luna, Chat Completions rejects tools with any effort but none.
    // Reasoning with tools needs /v1/responses, which stores by default.
    private static ChatOptions WithToolsNoChatCompletionsStoreNoReasoning() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            RawRepresentationFactory = _ => new ChatCompletionOptions { StoredOutputEnabled = false },
        };

    // Shared by every OpenAI-compatible provider; one that isn't brings its own.
    //
    // Verified against Microsoft.Extensions.AI.OpenAI 10.9.0 by capturing the
    // request body — re-check both on upgrade:
    //   ChatOptions.ModelId must stay unset: it silently overrides the id given here.
    //   ChatOptions.Instructions becomes exactly one system message, and none when
    //   unset. A note rather than a test, because moving to the Responses API
    //   would change the body without changing the behaviour.
    private static Func<IServiceProvider, IChatClient> OpenAiCompatibleChat(
        string providerCredential,
        Uri endpoint,
        string modelId)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = endpoint };

        return serviceProvider => new OpenAIClient(new ApiKeyCredential(providerCredential), clientOptions)
            .GetChatClient(modelId)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build(serviceProvider);
    }
}

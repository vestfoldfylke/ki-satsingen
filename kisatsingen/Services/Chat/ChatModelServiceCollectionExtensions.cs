using System.ClientModel;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using OpenAI;

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

    private static readonly ChatModelKey DefaultModelKey = ChatModelKeys.Fast;

    // PLACEHOLDER — display copy, context window and icon are all provisional.
    private static readonly ChatModel FastModel = new()
    {
        Key = ChatModelKeys.Fast,
        DisplayName = "Rask",
        ShortDescription = "Rask og rimelig. Passer til de fleste spørsmål.",
        LongDescription =
            "Svarer raskt og holder god kvalitet på vanlige oppgaver: oppsummering, omskriving, korte forklaringer "
            + "og enkle spørsmål. Velg en annen modell hvis du trenger grundig resonnering over et langt eller "
            + "komplisert underlag.",
        Provider = "OpenAI",
        ModelId = "gpt-4o-mini",
        ContextWindowTokens = 128_000,
        IconName = "placeholder-fast"
    };

    // PLACEHOLDER — display copy, context window and icon are all provisional.
    private static readonly ChatModel LargeModel = new()
    {
        Key = ChatModelKeys.Large,
        DisplayName = "Kompleks",
        ShortDescription = "Grundigere. Passer når oppgaven krever resonnering.",
        LongDescription =
            "Bruker lengre tid, men holder bedre på tråden i sammensatte oppgaver: analyse av lange dokumenter, "
            + "flersteg-resonnering og oppgaver der detaljene henger sammen. Velg den raske modellen til korte "
            + "spørsmål — denne er tregere uten å svare bedre på dem.",
        Provider = "Mistral",
        ModelId = "mistral-large-latest",
        ContextWindowTokens = 256_000,
        IconName = "placeholder-large"
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
            FastModel,
            OpenAiConfigurationPath,
            providerCredential => OpenAiCompatible(providerCredential, OpenAiEndpoint, FastModel.ModelId));

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            LargeModel,
            MistralConfigurationPath,
            providerCredential => OpenAiCompatible(providerCredential, MistralEndpoint, LargeModel.ModelId));

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
        Func<string, Func<IServiceProvider, IChatClient>> createClientFactory)
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

        definitions.Add(new ChatModelDefinition(model, createClientFactory(providerCredential), WithTools()));
    }

    // The only way in, so no model reaches the catalogue without its tools.
    private static ChatOptions WithTools() => new() { Tools = [.. ChatTools.All] };

    // Shared by every OpenAI-compatible provider; one that isn't brings its own.
    //
    // Verified against Microsoft.Extensions.AI.OpenAI 10.9.0 by capturing the
    // request body — re-check both on upgrade:
    //   ChatOptions.ModelId must stay unset: it silently overrides the id given here.
    //   ChatOptions.Instructions becomes exactly one system message, and none when
    //   unset. A note rather than a test, because moving to the Responses API
    //   would change the body without changing the behaviour.
    private static Func<IServiceProvider, IChatClient> OpenAiCompatible(
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

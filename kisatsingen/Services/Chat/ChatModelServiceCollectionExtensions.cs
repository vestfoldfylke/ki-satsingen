using System.ClientModel;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace kisatsingen.Services.Chat;

// Which models exist is declared here, in code, so adding or repointing one is a
// reviewed change rather than a configuration edit. Configuration supplies only
// what must not live in the repository: credentials and endpoints.
//
// Lives outside Program.cs because the composition root is long enough already,
// and because this list grows every time a provider is added.
internal static class ChatModelServiceCollectionExtensions
{
    private const string OpenAiConfigurationPath = "OpenAI:ApiKey";
    private const string MistralConfigurationPath = "Mistral:ApiKey";

    // Not a secret and not environment-specific, so it lives here rather than in
    // configuration: it is a fact about who the provider is, the same as ModelId.
    // Stating it even where the SDK would default to it keeps every provider
    // declaring where it points, instead of one of them being the implicit case.
    //
    // Safe to state: verified against OpenAI 2.12.0 by capturing the request URI
    // with this set and unset — both produce
    // https://api.openai.com/v1/chat/completions — and the .NET SDK has no
    // OPENAI_BASE_URL environment override that writing it here would shadow.
    private static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");

    // Mistral serves an OpenAI-compatible chat-completions API here, which is why
    // it needs no adapter of its own — only a different endpoint and key.
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
        ContextWindowTokens = 128_000,
        IconName = "placeholder-large"
    };

    public static IServiceCollection AddChatModels(this IServiceCollection services)
    {
        // Built inside the factory rather than out here so the warnings below reach
        // the real logging pipeline. Program.cs resolves this during startup, so a
        // broken default still refuses to boot instead of surfacing as a failed turn.
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

    // A model whose credentials are absent is dropped rather than fatal: one
    // provider's missing configuration should cost that provider's models, not the
    // whole application. The default model is the exception, and the catalogue
    // enforces that by refusing to construct without it.
    private static void AddIfConfigured(
        List<ChatModelDefinition> definitions,
        IConfiguration configuration,
        ILogger logger,
        ChatModel model,
        string configurationPath,
        Func<string, Func<IServiceProvider, IChatClient>> createClientFactory,
        ChatOptions? template = null)
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

        definitions.Add(new ChatModelDefinition(model, createClientFactory(providerCredential), WithTools(template)));
    }

    // Every registration goes through here, so a model cannot reach the catalogue
    // without its tools. See ChatTools.All for why that is an invariant rather than
    // a per-model setting.
    private static ChatOptions WithTools(ChatOptions? template)
    {
        var options = template?.Clone() ?? new ChatOptions();
        options.Tools = [.. ChatTools.All];
        return options;
    }

    // OpenAI and Mistral both speak OpenAI-compatible chat completions, so they
    // share this. A provider that does not — Anthropic — brings its own factory
    // and changes nothing here.
    //
    // The endpoint is required rather than defaulted: every caller says where it
    // points, so the file can be read top to bottom without knowing which
    // provider the SDK happens to assume.
    //
    // The model id goes to GetChatClient and nowhere else. ChatOptions.ModelId must
    // stay unset: verified against Microsoft.Extensions.AI.OpenAI 10.9.0 by
    // capturing the outgoing request body, the options value silently overrides
    // this one whenever both are present.
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

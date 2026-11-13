using System.ClientModel;
using System.ClientModel.Primitives;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

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

    // Uses OpenAI's Responses format, so it goes through the OpenAI SDK.
    private static readonly Uri MistralEndpoint = new("https://api.mistral.ai/v1");

    private static readonly ChatModelKey DefaultModelKey = ChatModelKeys.OpenAI;

    private static readonly ChatModel MistralModel = new()
    {
        Key = ChatModelKeys.Mistral,
        DisplayName = "Mistral Large",
        ShortDescription = "Grundige svar, europeisk leverandør.",
        LongDescription =
            "Kjører hos en europeisk leverandør, så arbeidet blir i EU. "
            + "Resonnerer før den svarer, og holder styr på lange samtaler og store dokumenter.",
        Provider = "Mistral AI",
        ModelId = "mistral-large-4",
        ContextWindowTokens = 1_000_000,
        IconName = "placeholder-eu"
    };

    private static readonly ChatModel MistralSmallModel = new()
    {
        Key = ChatModelKeys.MistralSmall,
        DisplayName = "Mistral Small",
        ShortDescription = "Raske svar, europeisk leverandør.",
        LongDescription =
            "Kjører hos en europeisk leverandør, så arbeidet blir i EU. "
            + "Mindre og raskere enn Mistral Large, god på oppsummering, omskriving og korte forklaringer.",
        Provider = "Mistral AI",
        ModelId = "mistral-small-2603",
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

    // Prompt caching needs nothing from us on either model: tested on 2026-11-10,
    // both cached the repeated start of a chat on their own. OpenAI from 1,024
    // input tokens; Mistral from about 5,500, and prompt_cache_key did not increase
    // cache hits when testing. Cached input tokens are stored with every
    // turn, so caching can be observed there.
    private static IReadOnlyList<ChatModelDefinition> BuildDefinitions(IConfiguration configuration, ILogger logger)
    {
        var definitions = new List<ChatModelDefinition>();

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            OpenAIModel,
            OpenAiConfigurationPath,
            providerCredential => (
                OpenAiCompletionsChat(providerCredential, OpenAiEndpoint, OpenAIModel.ModelId),
                LunaOptions()));

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            MistralModel,
            MistralConfigurationPath,
            providerCredential => (
                MistralResponsesChat(providerCredential, MistralEndpoint, MistralModel.ModelId),
                MistralOptions()));

        AddIfConfigured(
            definitions,
            configuration,
            logger,
            MistralSmallModel,
            MistralConfigurationPath,
            providerCredential => (
                MistralResponsesChat(providerCredential, MistralEndpoint, MistralSmallModel.ModelId),
                MistralSmallOptions()));

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

    // For gpt-6-luna, Chat Completions rejects tools with any reasoning effort but
    // none, and reasoning with tools needs /v1/responses, which stores by default.
    //
    // store=false is already the default, but set so a default change can't start
    // retaining conversations. Through RawRepresentationFactory, since the adapter
    // drops AdditionalProperties.
    internal static ChatOptions LunaOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            RawRepresentationFactory = _ => new ChatCompletionOptions { StoredOutputEnabled = false },
        };

    // Mistral Large 4 always reasons and rejects effort none.
    //
    // store=false: Mistral's Responses API is stateless, so every round, tool rounds
    // included, resends the whole history rather than pointing at a previous response
    // (pinned by ChatModelOptionsRequestTests).
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static ChatOptions MistralOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
            RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        };
#pragma warning restore OPENAI001

    // Mistral Small 4 takes only effort none or high. High rather than none, so its
    // reasoning can be compared with Mistral Large's.
    //
    // store=false for the same reason as Mistral Large (pinned by ChatModelOptionsRequestTests).
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static ChatOptions MistralSmallOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
            RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        };
#pragma warning restore OPENAI001

    // For providers on OpenAI's Chat Completions API.
    internal static Func<IServiceProvider, IChatClient> OpenAiCompletionsChat(
        string providerCredential,
        Uri endpoint,
        string modelId,
        PipelineTransport? transport = null) =>
        serviceProvider =>
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = endpoint, Transport = transport };

            return AddToolInvocation(
                new OpenAIClient(new ApiKeyCredential(providerCredential), clientOptions)
                    .GetChatClient(modelId)
                    .AsIChatClient(),
                serviceProvider);
        };

    // The Responses API rather than Chat Completions: Mistral Large 4 always reasons
    // (it rejects reasoning_effort none), and on Chat Completions it mixes that
    // reasoning into the answer's content as parts the OpenAI SDK cannot parse. On
    // Responses the reasoning arrives as items of its own. Its responses are corrected
    // where they depart from what the OpenAI SDK reads.
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static Func<IServiceProvider, IChatClient> MistralResponsesChat(
        string providerCredential,
        Uri endpoint,
        string modelId,
        PipelineTransport? transport = null) =>
        serviceProvider =>
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = endpoint, Transport = transport };
            clientOptions.AddPolicy(
                new MistralCreatedAtPolicy(serviceProvider.GetRequiredService<ILogger<MistralCreatedAtPolicy>>()),
                PipelinePosition.PerCall);

            return AddToolInvocation(
                new OpenAIClient(new ApiKeyCredential(providerCredential), clientOptions)
                    .GetResponsesClient()
                    .AsIChatClient(modelId),
                serviceProvider);
        };
#pragma warning restore OPENAI001

    private static IChatClient AddToolInvocation(IChatClient client, IServiceProvider serviceProvider) =>
        client
            .AsBuilder()
            .UseFunctionInvocation(configure: invoking =>
                invoking.FunctionInvoker = new ToolInvoker(serviceProvider.GetRequiredService<ILogger<ToolInvoker>>()).InvokeAsync)
            .Build(serviceProvider);
}

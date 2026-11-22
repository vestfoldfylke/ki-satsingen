using System.ClientModel;
using System.ClientModel.Primitives;
using kisatsingen.AIFunctions;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

namespace kisatsingen.Services.Chat;

// Models live in code so changing one is a reviewed change; configuration holds only credentials.
internal static class ChatModelServiceCollectionExtensions
{
    private const string OpenAiConfigurationPath = "OpenAI:ApiKey";
    private const string MistralConfigurationPath = "Mistral:ApiKey";

    // The SDK default, stated so every provider declares where it points.
    private static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");

    // Mistral speaks OpenAI's Responses format, so it goes through the OpenAI SDK.
    private static readonly Uri MistralEndpoint = new("https://api.mistral.ai/v1");

    private static readonly ChatModelKey DefaultModelKey = ChatModelKeys.OpenAI;

    private static readonly ChatModel MistralModel = new()
    {
        Key = ChatModelKeys.Mistral,
        DisplayName = "Mistral Large",
        ShortDescription = "Grundige svar, europeisk leverandør.",
        LongDescription =
            "Kjører hos en europeisk leverandør, så arbeidet blir i EU. Resonnerer før den svarer, og holder styr på lange samtaler og store dokumenter.",
        Provider = ModelProviders.Mistral,
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
            "Kjører hos en europeisk leverandør, så arbeidet blir i EU. Mindre og raskere enn Mistral Large, god på oppsummering, omskriving og korte forklaringer.",
        Provider = ModelProviders.Mistral,
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
        Provider = ModelProviders.OpenAI,
        ModelId = "gpt-6-luna",
        ContextWindowTokens = 1_000_000,
        IconName = "placeholder-code"
    };

    public static IServiceCollection AddChatModels(this IServiceCollection services)
    {
        // In the factory, so its warnings reach the real logging pipeline.
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
                OpenAiResponsesChat(providerCredential, OpenAiEndpoint, OpenAIModel.ModelId),
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

    // Missing credentials drop that provider's models, not the whole app.
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

    // Reasoning, so the model decides before it writes; without reasoning, the
    // model can repeat its answer after every tool call.
    //
    // store=false keeps nothing at OpenAI, so the reasoning has to come back
    // encrypted to be sent back in a tool round. RawRepresentationFactory, because
    // the adapter drops AdditionalProperties.
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static ChatOptions LunaOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full },
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false,
                IncludedProperties = { IncludedResponseProperty.ReasoningEncryptedContent },
            },
        };
#pragma warning restore OPENAI001

    // Mistral Large 4 rejects effort none.
    //
    // store=false, or a tool round points at the previous response, which
    // Mistral's stateless API rejects.
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static ChatOptions MistralOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
            RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        };
#pragma warning restore OPENAI001

    // Mistral Small 4 takes only effort none or high; high, so its reasoning can
    // be compared with Mistral Large's. store=false as for Mistral Large.
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static ChatOptions MistralSmallOptions() =>
        new()
        {
            Tools = [.. ChatTools.All],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
            RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        };
#pragma warning restore OPENAI001

#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    internal static Func<IServiceProvider, IChatClient> OpenAiResponsesChat(
        string providerCredential,
        Uri endpoint,
        string modelId,
        PipelineTransport? transport = null) =>
        serviceProvider =>
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = endpoint, Transport = transport };

            return AddToolInvocation(
                new OpenAIClient(new ApiKeyCredential(providerCredential), clientOptions)
                    .GetResponsesClient()
                    .AsIChatClient(modelId),
                serviceProvider);
        };
#pragma warning restore OPENAI001

    // Responses, not Chat Completions: there Mistral mixes its reasoning into the
    // answer in a shape the OpenAI SDK cannot parse. MistralCreatedAtPolicy fixes
    // where Mistral's responses differ from what the SDK expects.
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

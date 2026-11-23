using System.ClientModel;
using System.ClientModel.Primitives;
using kisatsingen.Services;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

namespace kisatsingen.AIFunctions.WebSearch;

// The model the search_web tool searches with: fixed, and the same for every
// chat model, so search behaves the same whichever model the user picked. Kept
// apart from the chat models, so swapping it or its provider touches only
// this file.
internal sealed record WebSearchModel(string Provider, string ModelId, IChatClient Client, ChatOptions Options)
{
    public const string CredentialConfigurationPath = "WebSearch:OpenAIApiKey";
    private const string LunaModelId = "gpt-6-luna";

    // Results are weighted towards Norway.
    private const string CountryCode = "NO";

    // Searches and page opens in one search_web call, so a runaway search stops.
    private const int MaxProviderToolCalls = 10;

    private static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1");

    // The chat model answers the user; the search model only reports what the
    // sources say, so the two never both try to solve the task.
    private const string Instructions =
        """
        Search the web for the query you are given. Report what the sources say about it, briefly and in Norwegian, with a link to each source you use.
        Prefer official and primary sources, such as Lovdata, the government and public agencies. Give the date of the sources for topics that change over time.
        If the sources do not answer the query, say so. Report only what the sources say.
        """;

    // Null without a key: search is optional, so the app starts without it.
    public static WebSearchModel? FromConfiguration(IConfiguration configuration)
    {
        var providerCredential = configuration[CredentialConfigurationPath];
        if (string.IsNullOrWhiteSpace(providerCredential))
        {
            return null;
        }

        return Create(providerCredential, OpenAiEndpoint);
    }

    internal static WebSearchModel Create(string providerCredential, Uri endpoint, PipelineTransport? transport = null)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = endpoint, Transport = transport };

#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
        var client = new OpenAIClient(new ApiKeyCredential(providerCredential), clientOptions)
            .GetResponsesClient()
            .AsIChatClient(LunaModelId);
#pragma warning restore OPENAI001

        return new WebSearchModel(ModelProviders.OpenAI, LunaModelId, client, CreateOptions());
    }

    // Web search needs more reasoning than none or minimal to give good results.
    // OpenAI's web search tool is set only here, not also as MEAI's
    // HostedWebSearchTool: the adapter adds ChatOptions.Tools to these, and the
    // model would get two. A tool call is required, and web search is the only
    // tool, so the model always searches instead of answering from memory.
    private static ChatOptions CreateOptions() =>
        new()
        {
            Instructions = Instructions,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
            RawRepresentationFactory = _ =>
            {
                var options = new CreateResponseOptions
                {
                    StoredOutputEnabled = false,
                    MaxToolCallCount = MaxProviderToolCalls,
                    ToolChoice = ResponseToolChoice.CreateRequiredChoice(),
                };
                options.Tools.Add(ResponseTool.CreateWebSearchTool(
                    userLocation: WebSearchToolLocation.CreateApproximateLocation(country: CountryCode),
                    searchContextSize: WebSearchToolContextSize.Medium));
                options.IncludedProperties.Add(IncludedResponseProperty.WebSearchCallActionSources);
                return options;
            },
#pragma warning restore OPENAI001
        };
}

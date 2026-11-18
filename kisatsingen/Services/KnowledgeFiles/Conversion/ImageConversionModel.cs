using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// The model images are read with. Fixed, since a file is converted once and an
// assistant's files have no chat model. Kept apart from the chat models, so
// swapping it or its provider touches only this file.
internal sealed record ImageConversionModel(string Provider, string ModelId, IChatClient Client, ChatOptions Options, long MaxImageBytes)
{
    private const string CredentialConfigurationPath = "ImageConversion:MistralApiKey";
    private const string MistralSmallModelId = "mistral-small-2603";

    // Room for a dense screenshot's text; a longer answer is refused as cut off.
    private const int MaxOutputTokens = 8_192;

    // Mistral takes 20 MB per image, and its docs do not say whether that is
    // before or after base64, which adds a third: 14 MiB stays under 20 MB both
    // ways. Check the new provider's limit when swapping.
    private const long MistralMaxImageBytes = 14 * ByteSize.Megabyte;

    private static readonly Uri MistralEndpoint = new("https://api.mistral.ai/v1");

    // Reasoning off, and sent explicitly rather than left to the model's
    // default: on Chat Completions, Mistral 4's reasoning arrives as content
    // parts the OpenAI SDK cannot parse.
    public static ImageConversionModel FromConfiguration(IConfiguration configuration)
    {
        var providerCredential = configuration[CredentialConfigurationPath];
        if (string.IsNullOrWhiteSpace(providerCredential))
        {
            throw new InvalidOperationException(
                $"Configuration '{CredentialConfigurationPath}' is missing or empty. Uploaded images are read by {MistralSmallModelId}, so the app cannot start without it. Set it to a Mistral API key.");
        }

        var client = new OpenAIClient(new ApiKeyCredential(providerCredential), new OpenAIClientOptions { Endpoint = MistralEndpoint })
            .GetChatClient(MistralSmallModelId)
            .AsIChatClient();

        var options = new ChatOptions
        {
            MaxOutputTokens = MaxOutputTokens,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
        };

        return new ImageConversionModel(ModelProviders.Mistral, MistralSmallModelId, client, options, MistralMaxImageBytes);
    }
}

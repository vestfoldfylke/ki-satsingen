using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

internal static class ChatMessageMapper
{
    private static readonly JsonSerializerOptions ContentsJson = AIJsonUtilities.DefaultOptions;

    // Read from the assembly that owns AIContent rather than maintained by hand:
    // the format we depend on is theirs, so we would never know to bump a number
    // of our own — it would be stale exactly when a failure needed it.
    private static readonly string AiContentVersion =
        typeof(AIContent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AIContent).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    // Content is stored twice on purpose: ContentsJson keeps the structured parts
    // a turn was made of, Content keeps the plain text. AIContent is polymorphic
    // JSON from a fast-moving library, so a package upgrade can change its
    // discriminators and make old rows unreadable — and without a fallback a
    // single such row would throw on every attempt to open the chat, putting the
    // whole conversation permanently out of reach. Degrading to the text is worth
    // far more than the structure it loses.
    public static ChatMessage FromEntity(Data.Entities.ChatMessage stored, ILogger logger)
    {
        var role = new ChatRole(stored.Role);

        if (string.IsNullOrEmpty(stored.ContentsJson))
        {
            return new ChatMessage(role, stored.Content);
        }

        List<AIContent>? contents;
        try
        {
            contents = JsonSerializer.Deserialize<List<AIContent>>(stored.ContentsJson, ContentsJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Stored message contents for {MessageId} could not be read and were replaced by their plain text. Tool calls and results in this message will not render. Written by Microsoft.Extensions.AI {WrittenBy}, read with {ReadWith} — if those differ, the AIContent JSON shape changed between them and the stored rows need migrating.",
                stored.Id,
                stored.ContentsSchemaVersion ?? "an unrecorded version",
                AiContentVersion);

            return new ChatMessage(role, stored.Content);
        }

        return contents is { Count: > 0 }
            ? new ChatMessage(role, contents)
            : new ChatMessage(role, stored.Content);
    }

    public static Data.Entities.ChatMessage ToEntity(ChatMessage message, string? systemPromptSnapshot = null) => new()
    {
        Role = message.Role.Value,
        Content = message.Text,
        ContentsJson = JsonSerializer.Serialize(message.Contents, ContentsJson),
        ContentsSchemaVersion = AiContentVersion,
        SystemPromptSnapshot = systemPromptSnapshot
    };

    // modelKey is ours, response.ModelId is the provider's; both are recorded
    // because neither can be derived from the other. See ChatMessage.ModelKey.
    public static Data.Entities.ChatMessage ToEntity(
        ChatMessage message,
        ChatResponse response,
        ChatModelKey modelKey,
        long durationMs,
        long? firstTokenMs,
        bool includeUsage)
    {
        var isAssistant = message.Role == ChatRole.Assistant;
        return new Data.Entities.ChatMessage
        {
            Role = message.Role.Value,
            Content = message.Text,
            ContentsJson = JsonSerializer.Serialize(message.Contents, ContentsJson),
            ContentsSchemaVersion = AiContentVersion,
            ResponseId = isAssistant ? response.ResponseId : null,
            ModelId = isAssistant ? response.ModelId : null,
            ModelKey = isAssistant ? modelKey.Value : null,
            FinishReason = isAssistant ? response.FinishReason?.Value : null,
            InputTokens = isAssistant && includeUsage ? response.Usage?.InputTokenCount : null,
            OutputTokens = isAssistant && includeUsage ? response.Usage?.OutputTokenCount : null,
            TotalTokens = isAssistant && includeUsage ? response.Usage?.TotalTokenCount : null,
            DurationMs = isAssistant ? durationMs : null,
            TimeToFirstTokenMs = isAssistant ? firstTokenMs : null
        };
    }
}

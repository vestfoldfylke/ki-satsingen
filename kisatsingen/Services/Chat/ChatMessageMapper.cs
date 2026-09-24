using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

internal static class ChatMessageMapper
{
    private static readonly JsonSerializerOptions ContentsJson = AIJsonUtilities.DefaultOptions;

    // Read from their assembly, not kept by hand: the format is theirs, so a
    // number of our own would be stale exactly when a failure needed it.
    private static readonly string AiContentVersion =
        typeof(AIContent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AIContent).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    // Falls back to the plain-text copy because AIContent's polymorphic JSON can
    // change shape across package upgrades, and one unreadable row would otherwise
    // lock the user out of the whole chat.
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

    // Both modelKey and response.ModelId are recorded: neither derives from the other.
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

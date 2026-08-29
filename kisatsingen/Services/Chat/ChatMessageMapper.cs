using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

internal static class ChatMessageMapper
{
    private static readonly JsonSerializerOptions ContentsJson = AIJsonUtilities.DefaultOptions;

    public static ChatMessage FromEntity(Data.Entities.ChatMessage stored)
    {
        var role = new ChatRole(stored.Role);

        if (string.IsNullOrEmpty(stored.ContentsJson))
        {
            return new ChatMessage(role, stored.Content);
        }

        var contents = JsonSerializer.Deserialize<List<AIContent>>(stored.ContentsJson, ContentsJson);
        return contents is { Count: > 0 }
            ? new ChatMessage(role, contents)
            : new ChatMessage(role, stored.Content);
    }

    public static Data.Entities.ChatMessage ToEntity(ChatMessage message) => new()
    {
        Role = message.Role.Value,
        Content = message.Text,
        ContentsJson = JsonSerializer.Serialize(message.Contents, ContentsJson)
    };

    public static Data.Entities.ChatMessage ToEntity(
        ChatMessage message,
        ChatResponse response,
        long durationMs,
        long? firstTokenMs)
    {
        var isAssistant = message.Role == ChatRole.Assistant;
        return new Data.Entities.ChatMessage
        {
            Role = message.Role.Value,
            Content = message.Text,
            ContentsJson = JsonSerializer.Serialize(message.Contents, ContentsJson),
            ResponseId = isAssistant ? response.ResponseId : null,
            ModelId = isAssistant ? response.ModelId : null,
            FinishReason = isAssistant ? response.FinishReason?.Value : null,
            InputTokens = isAssistant ? response.Usage?.InputTokenCount : null,
            OutputTokens = isAssistant ? response.Usage?.OutputTokenCount : null,
            TotalTokens = isAssistant ? response.Usage?.TotalTokenCount : null,
            DurationMs = isAssistant ? durationMs : null,
            TimeToFirstTokenMs = isAssistant ? firstTokenMs : null
        };
    }
}

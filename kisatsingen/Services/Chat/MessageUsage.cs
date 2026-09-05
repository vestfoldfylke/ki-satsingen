namespace kisatsingen.Services.Chat;

public sealed record MessageUsage(long? InputTokens, long? OutputTokens, long? TotalTokens)
{
    public static MessageUsage? FromEntity(Data.Entities.ChatMessage entity)
    {
        if (entity.InputTokens is null && entity.OutputTokens is null && entity.TotalTokens is null)
        {
            return null;
        }

        return new MessageUsage(entity.InputTokens, entity.OutputTokens, entity.TotalTokens);
    }
}

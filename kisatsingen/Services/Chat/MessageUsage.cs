namespace kisatsingen.Services.Chat;

public sealed record MessageUsage(long? InputTokens, long? OutputTokens, long? TotalTokens)
{
    public static MessageUsage? FromCounts(long? inputTokens, long? outputTokens, long? totalTokens) =>
        inputTokens is null && outputTokens is null && totalTokens is null
            ? null
            : new MessageUsage(inputTokens, outputTokens, totalTokens);
}

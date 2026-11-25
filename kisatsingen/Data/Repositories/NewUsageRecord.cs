using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Data.Repositories;

// Built only through the factories, one per kind of spend, so each row carries
// exactly the links that kind has.
public sealed record NewUsageRecord
{
    private NewUsageRecord(
        UsageKind kind,
        Guid userId,
        Guid? chatId,
        Guid? turnId,
        Guid? assistantId,
        string provider,
        string modelId,
        MessageUsage usage,
        TurnStatus? status,
        int? webSearchCount)
    {
        Kind = kind;
        UserId = userId;
        ChatId = chatId;
        TurnId = turnId;
        AssistantId = assistantId;
        Provider = provider;
        ModelId = modelId;
        Usage = usage;
        Status = status;
        WebSearchCount = webSearchCount;
    }

    public UsageKind Kind { get; }
    public Guid UserId { get; }
    public Guid? ChatId { get; }
    public Guid? TurnId { get; }
    public Guid? AssistantId { get; }
    public string Provider { get; }
    public string ModelId { get; }
    public MessageUsage Usage { get; }
    public TurnStatus? Status { get; }
    public int? WebSearchCount { get; }

    public static NewUsageRecord ForTurn(
        Guid userId,
        Guid chatId,
        Guid turnId,
        Guid? assistantId,
        string provider,
        string modelId,
        MessageUsage usage,
        TurnStatus status) =>
        new(
            kind: UsageKind.ChatTurn,
            userId: userId,
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            provider: provider,
            modelId: modelId,
            usage: usage,
            status: status,
            webSearchCount: null);

    // assistantId is the chat's assistant, as on the turn's own row.
    public static NewUsageRecord ForChatFileConversion(Guid userId, Guid chatId, Guid turnId, Guid? assistantId, ConversionTokenUsage tokenUsage) =>
        new(
            kind: UsageKind.KnowledgeFileConversion,
            userId: userId,
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            provider: tokenUsage.Provider,
            modelId: tokenUsage.ModelId,
            usage: tokenUsage.Usage,
            status: null,
            webSearchCount: null);

    public static NewUsageRecord ForAssistantFileConversion(Guid userId, Guid assistantId, ConversionTokenUsage tokenUsage) =>
        new(
            kind: UsageKind.KnowledgeFileConversion,
            userId: userId,
            chatId: null,
            turnId: null,
            assistantId: assistantId,
            provider: tokenUsage.Provider,
            modelId: tokenUsage.ModelId,
            usage: tokenUsage.Usage,
            status: null,
            webSearchCount: null);

    // One row for all the searches in a turn, like the turn's own row sums all
    // its model calls. assistantId is the chat's assistant.
    public static NewUsageRecord ForWebSearch(
        Guid userId,
        Guid chatId,
        Guid turnId,
        Guid? assistantId,
        string provider,
        string modelId,
        MessageUsage usage,
        int webSearchCount) =>
        new(
            kind: UsageKind.WebSearch,
            userId: userId,
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            provider: provider,
            modelId: modelId,
            usage: usage,
            status: null,
            webSearchCount: webSearchCount);
}

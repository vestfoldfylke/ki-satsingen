using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Data.Repositories;

// Built only through the factories, one per kind of spend, so each row carries
// exactly the links that kind has.
public sealed record TokenUsageEntry
{
    private TokenUsageEntry(
        TokenUsageKind kind,
        Guid userId,
        Guid? chatId,
        Guid? turnId,
        Guid? assistantId,
        string provider,
        string modelId,
        MessageUsage usage,
        TurnStatus? status)
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
    }

    public TokenUsageKind Kind { get; }
    public Guid UserId { get; }
    public Guid? ChatId { get; }
    public Guid? TurnId { get; }
    public Guid? AssistantId { get; }
    public string Provider { get; }
    public string ModelId { get; }
    public MessageUsage Usage { get; }
    public TurnStatus? Status { get; }

    public static TokenUsageEntry ForTurn(
        Guid userId,
        Guid chatId,
        Guid turnId,
        Guid? assistantId,
        string provider,
        string modelId,
        MessageUsage usage,
        TurnStatus status) =>
        new(
            kind: TokenUsageKind.ChatTurn,
            userId: userId,
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            provider: provider,
            modelId: modelId,
            usage: usage,
            status: status);

    // assistantId is the chat's assistant, as on the turn's own row.
    public static TokenUsageEntry ForChatFileConversion(Guid userId, Guid chatId, Guid turnId, Guid? assistantId, ConversionTokenUsage tokenUsage) =>
        new(
            kind: TokenUsageKind.KnowledgeFileConversion,
            userId: userId,
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            provider: tokenUsage.Provider,
            modelId: tokenUsage.ModelId,
            usage: tokenUsage.Usage,
            status: null);

    public static TokenUsageEntry ForAssistantFileConversion(Guid userId, Guid assistantId, ConversionTokenUsage tokenUsage) =>
        new(
            kind: TokenUsageKind.KnowledgeFileConversion,
            userId: userId,
            chatId: null,
            turnId: null,
            assistantId: assistantId,
            provider: tokenUsage.Provider,
            modelId: tokenUsage.ModelId,
            usage: tokenUsage.Usage,
            status: null);
}

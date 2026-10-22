using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.Chat;

// One send, as the runner needs it. The request to the model is built from
// EarlierTurns and the turn only once its attachments are settled, since the
// attachment line needs each saved file's id.
internal sealed record TurnInput(
    Turn Turn,
    Guid ChatId,
    bool IsChatSaved,
    IReadOnlyList<Turn> EarlierTurns,
    ChatModel Model,
    SentAttachments SentAttachments,
    bool ChatHasKnowledgeFiles);

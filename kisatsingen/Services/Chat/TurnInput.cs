using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.Chat;

// One send, as the runner needs it. The request to the model is built from
// EarlierTurns and the turn only once its attachments are settled, since the
// attachment line needs each saved file's id.
//
// The runner only borrows SentAttachments: the send that took them releases
// them.
internal sealed record TurnInput(
    Turn Turn,
    Guid? ExistingChatId,
    IReadOnlyList<Turn> EarlierTurns,
    ChatModel Model,
    SentAttachments SentAttachments,
    bool ChatHasKnowledgeFiles);

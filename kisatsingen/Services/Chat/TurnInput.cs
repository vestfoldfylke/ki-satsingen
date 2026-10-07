namespace kisatsingen.Services.Chat;

// One send, as the runner needs it. The request to the model is built from
// EarlierTurns and the turn only once its attachments are settled, since the
// attachment line needs each saved file's id.
//
// MessageAttachments are taken only after the message is saved, so a turn
// that fails before that leaves them in the composer.
internal sealed record TurnInput(
    Turn Turn,
    Guid? ExistingChatId,
    IReadOnlyList<Turn> EarlierTurns,
    ChatModel Model,
    MessageAttachments MessageAttachments,
    bool ChatHasKnowledgeFiles);

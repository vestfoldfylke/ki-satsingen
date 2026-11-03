namespace kisatsingen.Services.KnowledgeFiles.Reading;

// The files one chat's tools may reach: the chat's own and, in a chat with an
// assistant, that assistant's. AssistantId is the chat's, never one the model
// names.
public sealed record KnowledgeFileScope(Guid UserId, Guid ChatId, Guid? AssistantId);

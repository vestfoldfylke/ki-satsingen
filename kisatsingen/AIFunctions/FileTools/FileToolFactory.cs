using kisatsingen.Services.KnowledgeFiles.Reading;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.FileTools;

// Owner and chat are fixed when the tools are made, so nothing the model sends
// can widen what they reach: a file outside the chat is simply not found.
public sealed class FileToolFactory(IKnowledgeFileReader reader, FileToolOptions options)
{
    public IReadOnlyList<AITool> CreateForChat(string ownerId, Guid chatId) =>
    [
        ListFilesTool.Create(reader, ownerId, chatId),
        GetOutlineTool.Create(reader, options, ownerId, chatId),
        ReadFileTool.Create(reader, options, ownerId, chatId)
    ];
}

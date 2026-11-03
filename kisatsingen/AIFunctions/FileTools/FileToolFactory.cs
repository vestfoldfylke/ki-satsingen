using kisatsingen.Services.KnowledgeFiles.Reading;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.FileTools;

// The scope is fixed when the tools are made, so nothing the model sends can
// widen what they reach: a file outside it is simply not found.
public sealed class FileToolFactory(IKnowledgeFileReader reader, FileToolOptions options)
{
    public IReadOnlyList<AITool> Create(KnowledgeFileScope scope) =>
    [
        ListFilesTool.Create(reader, scope),
        GetOutlineTool.Create(reader, options, scope),
        ReadFileTool.Create(reader, options, scope)
    ];
}

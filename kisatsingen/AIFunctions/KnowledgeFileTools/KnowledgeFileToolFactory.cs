using kisatsingen.Services.KnowledgeFiles.Reading;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.KnowledgeFileTools;

// The scope is fixed when the tools are made, so nothing the model sends can
// widen what they reach: a file outside it is simply not found.
public sealed class KnowledgeFileToolFactory(IKnowledgeFileReader reader, KnowledgeFileToolOptions options)
{
    public static readonly IReadOnlySet<string> ToolNames = new HashSet<string> { ListFilesTool.Name, GetOutlineTool.Name, ReadFileTool.Name };

    public IReadOnlyList<AITool> Create(KnowledgeFileScope scope) =>
    [
        ListFilesTool.Create(reader, scope),
        GetOutlineTool.Create(reader, options, scope),
        ReadFileTool.Create(reader, options, scope)
    ];
}

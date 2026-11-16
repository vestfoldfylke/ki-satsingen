using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.PresentationTools;

public sealed class PresentationToolFactory
{
    public static readonly IReadOnlySet<string> ToolNames = new HashSet<string> { CreatePresentationTool.Name };

    public IReadOnlyList<AITool> Create() =>
    [
        CreatePresentationTool.Create()
    ];
}

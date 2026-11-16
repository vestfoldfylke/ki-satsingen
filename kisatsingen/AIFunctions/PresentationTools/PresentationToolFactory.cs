using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.PresentationTools;

public sealed class PresentationToolFactory
{
    public IReadOnlyList<AITool> Create() =>
    [
        CreatePresentationTool.Create()
    ];
}

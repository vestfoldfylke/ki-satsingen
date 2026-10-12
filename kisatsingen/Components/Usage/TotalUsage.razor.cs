using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage;

public partial class TotalUsage : ComponentBase
{
    [Parameter]
    public long TotalInputTokenUsage { get; init; }

    [Parameter]
    public long TotalOutputTokenUsage { get; init; }

    [Parameter]
    public long TotalEstimatedInputTokenUsage { get; init; }

    [Parameter]
    public long TotalEstimatedOutputTokenUsage { get; init; }
}
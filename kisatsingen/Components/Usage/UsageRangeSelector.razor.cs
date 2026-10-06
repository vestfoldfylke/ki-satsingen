using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage;

public partial class UsageRangeSelector : ComponentBase
{
    private static readonly UsageRange[] Ranges =
    [
        UsageRange.Last7Days,
        UsageRange.Last30Days,
        UsageRange.Last90Days,
        UsageRange.All
    ];

    [Parameter]
    public UsageRange Current { get; set; } = UsageRange.Last7Days;

    [Parameter]
    public EventCallback<UsageRange> CurrentChanged { get; set; }

    private Task SelectionChanged() => CurrentChanged.InvokeAsync(Current);
}

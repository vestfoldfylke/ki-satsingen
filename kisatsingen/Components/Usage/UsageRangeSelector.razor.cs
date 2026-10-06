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
    public UsageRange Current { get; set; }

    [Parameter]
    public EventCallback<UsageRange> CurrentChanged { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    private Task OnChange(UsageRange range)
    {
        if (range == Current)
        {
            return Task.CompletedTask;
        }

        Current = range;
        return CurrentChanged.InvokeAsync(range);
    }
}

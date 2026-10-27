using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class TopUsersByTurnsChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UserUsage> TopUsers { get; set; } = [];

    private IReadOnlyList<UserTurnPoint> _points = [];

    protected override void OnParametersSet()
    {
        _points = TopUsers
            .Select(row => new UserTurnPoint(row.DisplayName, row.Usage.TurnCount))
            .ToList();
    }

    private sealed record UserTurnPoint(string Name, int TurnCount);
}

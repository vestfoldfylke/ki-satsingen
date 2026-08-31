using kisatsingen.Constants;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services;

internal sealed class BlazorCircuitObserver(IMetricsService metrics) : CircuitHandler
{
    private static readonly string Prefix = $"{MetricConstants.MetricsAppPrefix}_Circuit";

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_Opened", "Blazor Server circuits opened");
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_Closed", "Blazor Server circuits closed");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_ConnectionDown", "SignalR transport lost while a circuit is still alive");
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_ConnectionUp", "SignalR transport restored on an existing circuit");
        return Task.CompletedTask;
    }
}

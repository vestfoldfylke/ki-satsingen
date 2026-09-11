using kisatsingen.Constants;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services;

// Counts circuit lifecycle events, and forwards the one of them that something
// else needs to act on. Registered scoped alongside ChatSession, so the session
// injected here is the same instance the page is driving.
internal sealed class BlazorCircuitObserver(IMetricsService metrics, ChatSession session) : CircuitHandler
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

        // Told directly rather than left for ChatClientChannel to discover. The
        // channel only finds out when it next tries to push a token, so a turn
        // that is producing nothing right now — sitting in a tool call, waiting on
        // the first token — would keep running against a browser that stopped
        // listening, for as long as it takes to produce something to fail on.
        session.CancelForDisconnect();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_ConnectionUp", "SignalR transport restored on an existing circuit");
        return Task.CompletedTask;
    }
}

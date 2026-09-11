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
        // Deliberately does not cancel the turn. The circuit is still alive and
        // Blazor retains it so the client can reconnect to it; the turn keeps
        // running and persisting, and ChatClientChannel simply stops pushing
        // tokens nobody can receive. Cancelling here would destroy an in-flight
        // answer over a blip the framework is built to recover from.
        metrics.Count($"{Prefix}_ConnectionDown", "SignalR transport lost while a circuit is still alive");
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_ConnectionUp", "SignalR transport restored on an existing circuit");

        // Lifts the delivery pause set on the way down. Without this the channel
        // stays latched and every later turn on this circuit streams nothing,
        // arriving in one lump when it commits instead.
        session.ResumeStreaming();
        return Task.CompletedTask;
    }
}

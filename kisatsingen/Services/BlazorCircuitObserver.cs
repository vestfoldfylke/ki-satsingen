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
        // Pauses delivery, not the turn. The circuit is still alive and Blazor
        // retains it so the client can reconnect; the turn keeps running and
        // persisting, and only the push of tokens nobody can receive stops.
        // Cancelling here would destroy an in-flight answer over a blip the
        // framework is built to recover from.
        metrics.Count($"{Prefix}_ConnectionDown", "SignalR transport lost while a circuit is still alive");
        session.PauseDelivery();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        metrics.Count($"{Prefix}_ConnectionUp", "SignalR transport restored on an existing circuit");

        // Lifts the pause set on the way down. Without this the channel stays
        // muted and every later turn on this circuit streams nothing, arriving in
        // one lump when it commits instead.
        session.ResumeDelivery();
        return Task.CompletedTask;
    }
}

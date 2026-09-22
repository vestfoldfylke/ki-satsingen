using kisatsingen.Constants;

namespace kisatsingen.Services.Chat;

// How one press of send ended. Exactly one per turn, decided in whichever branch
// of ChatSession.SendAsync the turn left through and counted once in its finally.
//
// Not ChatEventKind: that is what the transcript shows, and a successful or
// unauthenticated turn shows nothing. This is what the turn was.
internal enum TurnOutcome
{
    Success,
    Stopped,
    Disconnected,
    Unauthenticated,
    Failed
}

internal static class TurnOutcomeMetric
{
    // The label values are what dashboards and alerts are keyed on, so they are
    // mapped here rather than derived from the enum names: renaming a member must
    // not silently rename a time series.
    public static string LabelValue(TurnOutcome outcome) => outcome switch
    {
        TurnOutcome.Success => MetricConstants.MetricsResultSuccessLabelValue,
        TurnOutcome.Stopped => MetricConstants.MetricsResultCancelledLabelValue,
        TurnOutcome.Disconnected => MetricConstants.MetricsResultDisconnectedLabelValue,
        TurnOutcome.Unauthenticated => MetricConstants.MetricsResultUnauthenticatedLabelValue,
        TurnOutcome.Failed => MetricConstants.MetricsResultFailedLabelValue,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Every outcome needs a metric label; add one to TurnOutcomeMetric.")
    };
}

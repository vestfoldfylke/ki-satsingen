using kisatsingen.Constants;

namespace kisatsingen.Services.Chat;

// How a send ended. Not ChatEventKind: that is what the transcript shows, and
// success or an unauthenticated attempt shows nothing.
internal enum TurnOutcome
{
    Success,
    Stopped,
    LeftChat,
    Disconnected,
    Unauthenticated,
    Failed
}

internal static class TurnOutcomeMetric
{
    // Mapped explicitly, so renaming a member can't silently rename a time series.
    public static string LabelValue(TurnOutcome outcome) => outcome switch
    {
        TurnOutcome.Success => MetricConstants.MetricsResultSuccessLabelValue,
        TurnOutcome.Stopped => MetricConstants.MetricsResultCancelledLabelValue,
        TurnOutcome.LeftChat => MetricConstants.MetricsResultLeftChatLabelValue,
        TurnOutcome.Disconnected => MetricConstants.MetricsResultDisconnectedLabelValue,
        TurnOutcome.Unauthenticated => MetricConstants.MetricsResultUnauthenticatedLabelValue,
        TurnOutcome.Failed => MetricConstants.MetricsResultFailedLabelValue,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Every outcome needs a metric label; add one to TurnOutcomeMetric.")
    };
}

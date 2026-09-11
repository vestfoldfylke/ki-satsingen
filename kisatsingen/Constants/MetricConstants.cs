namespace kisatsingen.Constants;

public static class MetricConstants
{
    public static string MetricsAppPrefix => "KIWeb";

    public static string MetricsModelLabelName => "Model";

    // Reported when the model that served a turn is not known — a turn that was
    // stopped or failed before a response came back.
    public static string MetricsModelUnknownLabelValue => "unknown";

    public static string MetricsResultLabelName => "Result";
    public static string MetricsResultSuccessLabelValue => "Success";

    // Kept apart from Failed: the user pressing stop is an intended outcome, and
    // counting it as an error would put expected behaviour into failure alerts.
    public static string MetricsResultCancelledLabelValue => "Cancelled";

    // The circuit was lost mid-turn. Not a user stop and not a server failure —
    // an alert on this bucket says something about connectivity, not the app.
    public static string MetricsResultDisconnectedLabelValue => "Disconnected";

    // Rejected before the turn began, so it never reached the model. Separate
    // from Failed so an authentication problem is not read as the chat breaking.
    public static string MetricsResultUnauthenticatedLabelValue => "Unauthenticated";

    // The turn broke for a reason the user neither asked for nor can fix. The one
    // bucket here worth alerting on.
    public static string MetricsResultFailedLabelValue => "Failed";

    // Which step of a turn failed. It lives on its own counter rather than as a
    // third label on the outcome counter: a metric's label names are fixed by its
    // first use, so a stage that only means something for failures would have to
    // be reported as a filler value by every success.
    public static string MetricsStageLabelName => "Stage";

    // The .NET type name of the exception, never its message. A type name is a
    // bounded set drawn from the code; a message carries ids, paths and provider
    // error bodies, and would make the series cardinality unbounded with it.
    public static string MetricsExceptionLabelName => "Exception";
}
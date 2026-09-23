namespace kisatsingen.Constants;

public static class MetricConstants
{
    public static string MetricsAppPrefix => "KIWeb";

    public static string MetricsModelLabelName => "Model";

    // Stable, unlike Model, which changes whenever a provider rolls a dated build.
    // Group dashboards by this one.
    public static string MetricsModelKeyLabelName => "ModelKey";

    // A turn that ended before a response came back.
    public static string MetricsModelUnknownLabelValue => "unknown";

    public static string MetricsResultLabelName => "Result";
    public static string MetricsResultSuccessLabelValue => "Success";

    // Apart from Failed, so pressing stop never reaches failure alerts.
    public static string MetricsResultCancelledLabelValue => "Cancelled";

    // Says something about connectivity, not the app.
    public static string MetricsResultDisconnectedLabelValue => "Disconnected";

    // Apart from Failed, so an auth problem isn't read as the chat breaking.
    public static string MetricsResultUnauthenticatedLabelValue => "Unauthenticated";

    // The one bucket worth alerting on.
    public static string MetricsResultFailedLabelValue => "Failed";

    // Its own counter rather than a third outcome label: label names are fixed on
    // first use, so every success would need a filler stage.
    public static string MetricsStageLabelName => "Stage";

    // The type name, never the message: messages carry ids and provider bodies, and
    // would make the series cardinality unbounded.
    public static string MetricsExceptionLabelName => "Exception";
}

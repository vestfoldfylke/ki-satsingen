namespace kisatsingen.Constants;

public static class MetricConstants
{
    // Names follow https://prometheus.io/docs/practices/naming/
    public static string MetricsAppPrefix => "kiweb";

    public static string MetricsModelLabelName => "model";

    // Stable, unlike model, which changes whenever a provider rolls a dated build.
    // Group dashboards by this one.
    public static string MetricsModelKeyLabelName => "model_key";

    // A turn that ended before a response came back.
    public static string MetricsModelUnknownLabelValue => "unknown";

    public static string MetricsResultLabelName => "result";
    public static string MetricsResultSuccessLabelValue => "success";

    // Apart from failed, so pressing stop never reaches failure alerts.
    public static string MetricsResultCancelledLabelValue => "cancelled";

    // Apart from cancelled: how often this happens decides whether turns should
    // keep running in the background.
    public static string MetricsResultLeftChatLabelValue => "left_chat";

    // Says something about connectivity, not the app.
    public static string MetricsResultDisconnectedLabelValue => "disconnected";

    // Apart from failed, so an auth problem isn't read as the chat breaking.
    public static string MetricsResultUnauthenticatedLabelValue => "unauthenticated";

    // The one bucket worth alerting on.
    public static string MetricsResultFailedLabelValue => "failed";

    // Its own counter rather than a third outcome label: label names are fixed on
    // first use, so every success would need a filler stage.
    public static string MetricsStageLabelName => "stage";

    // The type name, never the message: messages carry ids and provider bodies, and
    // would make the series cardinality unbounded.
    public static string MetricsExceptionLabelName => "exception";

    public static string MetricsToolLabelName => "tool";

    // A rejection key, written out beside its message: never the message itself.
    public static string MetricsReasonLabelName => "reason";

    // A type name, so the package bounds it: never anything the model wrote.
    public static string MetricsContentTypeLabelName => "type";
}

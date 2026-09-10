namespace kisatsingen.Constants;

public static class MetricConstants
{
    public static string MetricsAppPrefix => "KISatsingen";

    public static string MetricsModelLabelName => "Model";

    // Reported when the model that served a turn is not known — a turn that was
    // stopped or failed before a response came back.
    public static string MetricsModelUnknownLabelValue => "unknown";

    public static string MetricsResultLabelName => "Result";
    public static string MetricsResultSuccessLabelValue => "Success";

    // Kept apart from Failed: the user pressing stop is an intended outcome, and
    // counting it as an error would put expected behaviour into failure alerts.
    public static string MetricsResultCancelledLabelValue => "Cancelled";

    // Rejected before the turn began, so it never reached the model. Separate
    // from Failed so an authentication problem is not read as the chat breaking.
    public static string MetricsResultUnauthenticatedLabelValue => "Unauthenticated";

    public static string MetricsResultFailedLabelValue => "Failed";
}
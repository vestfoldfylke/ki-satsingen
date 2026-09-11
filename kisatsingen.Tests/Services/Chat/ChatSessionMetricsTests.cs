using kisatsingen.Constants;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The outcome counter promises one thing: every press of send lands in exactly
// one Result bucket, so the buckets sum to the attempts. Dashboards and alerts
// are built on that, and it is a promise only the call sites can keep — which
// makes it worth pinning here rather than trusting to review.
public sealed class ChatSessionMetricsTests
{
    private const string SendCounter = "_Send";
    private const string FailureCounter = "_Failure";

    [Fact]
    public async Task A_turn_that_answers_counts_one_success()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        AssertSingleResult(harness, MetricConstants.MetricsResultSuccessLabelValue);
    }

    [Fact]
    public async Task A_turn_that_breaks_counts_one_failure()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        AssertSingleResult(harness, MetricConstants.MetricsResultFailedLabelValue);
    }

    [Fact]
    public async Task A_stopped_turn_counts_one_cancellation()
    {
        await using var harness = new ChatSessionHarness();
        var reached = StallTheModel(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        await send;

        AssertSingleResult(harness, MetricConstants.MetricsResultCancelledLabelValue);
    }

    [Fact]
    public async Task A_turn_that_lost_its_circuit_counts_one_disconnect()
    {
        await using var harness = new ChatSessionHarness();
        var reached = StallTheModel(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.CancelForDisconnect();
        await send;

        AssertSingleResult(harness, MetricConstants.MetricsResultDisconnectedLabelValue);
    }

    // Rejected before the turn began, and still an attempt: a press of send that
    // went uncounted would break the sum as surely as one counted twice.
    [Fact]
    public async Task An_unauthenticated_attempt_counts_one_rejection()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));

        AssertSingleResult(harness, MetricConstants.MetricsResultUnauthenticatedLabelValue);
    }

    // The regression this suite exists for. Success used to be counted on entry
    // to the save, so a save that then failed reported Success and Failed for one
    // attempt — inflating the success rate on the very dashboard you would use to
    // judge whether the error handling works.
    [Fact]
    public async Task A_response_that_cannot_be_saved_is_not_also_counted_as_a_success()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.SecondAppendMessagesFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        AssertSingleResult(harness, MetricConstants.MetricsResultFailedLabelValue);
    }

    [Fact]
    public async Task A_turn_cancelled_while_saving_its_response_is_not_also_counted_as_a_success()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.SecondAppendMessagesFailure = new OperationCanceledException();
        harness.Session.Cancel();

        await harness.Session.SendAsync("hei");

        Assert.Single(harness.Metrics.Named(SendCounter));
    }

    [Fact]
    public async Task A_failure_reports_the_stage_it_reached_and_the_type_that_caused_it()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new TimeoutException("provider gave up"));

        await harness.Session.SendAsync("hei");

        var failure = Assert.Single(harness.Metrics.Named(FailureCounter));
        Assert.Equal(nameof(TurnStage.Generating), failure.Label(MetricConstants.MetricsStageLabelName));
        Assert.Equal(nameof(TimeoutException), failure.Label(MetricConstants.MetricsExceptionLabelName));
    }

    [Fact]
    public async Task A_failure_saving_the_response_is_reported_against_that_stage()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.SecondAppendMessagesFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        var failure = Assert.Single(harness.Metrics.Named(FailureCounter));
        Assert.Equal(nameof(TurnStage.SavingResponse), failure.Label(MetricConstants.MetricsStageLabelName));
    }

    // A timeout is a failure, so it must carry a stage and an exception type an
    // alert can read — not disappear into the cancellation bucket, which has no
    // Failure series at all.
    [Fact]
    public async Task A_provider_timeout_is_reported_as_a_failure_with_its_own_type()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new TaskCanceledException());

        await harness.Session.SendAsync("hei");

        AssertSingleResult(harness, MetricConstants.MetricsResultFailedLabelValue);
        var failure = Assert.Single(harness.Metrics.Named(FailureCounter));
        Assert.Equal(nameof(TaskCanceledException), failure.Label(MetricConstants.MetricsExceptionLabelName));
    }

    // Cancellation is an intended outcome, not a fault. Counting it on the
    // failure counter would put expected behaviour into failure alerts.
    [Fact]
    public async Task A_stopped_turn_is_absent_from_the_failure_counter()
    {
        await using var harness = new ChatSessionHarness();
        var reached = StallTheModel(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        await send;

        Assert.Empty(harness.Metrics.Named(FailureCounter));
    }

    private static void AssertSingleResult(ChatSessionHarness harness, string expected)
    {
        var send = Assert.Single(harness.Metrics.Named(SendCounter));
        Assert.Equal(expected, send.Label(MetricConstants.MetricsResultLabelName));
    }

    private static Task StallTheModel(ChatSessionHarness harness)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);
        return reached.Task;
    }
}

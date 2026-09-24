using kisatsingen.Constants;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// Every send lands in exactly one Result bucket, so the buckets sum to the
// attempts. Dashboards and alerts rely on that.
public sealed class ChatSessionMetricsTests
{
    private const string SendCounter = RecordingMetricsService.SendCounter;
    private const string FailureCounter = RecordingMetricsService.FailureCounter;

    // The count shares a finally with the teardown that unlocks the composer.
    [Fact]
    public async Task A_metrics_failure_does_not_leave_the_session_busy()
    {
        await using var harness = new ChatSessionHarness();
        harness.Metrics.ThrowForNameEndingWith = SendCounter;

        await harness.Session.SendAsync("hei");

        Assert.False(harness.Session.IsBusy);
    }

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

    // Still an attempt: uncounted breaks the sum as surely as counted twice.
    [Fact]
    public async Task An_unauthenticated_attempt_counts_one_rejection()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));

        AssertSingleResult(harness, MetricConstants.MetricsResultUnauthenticatedLabelValue);
    }

    // Success counted before a failing save would report two outcomes for one
    // send, inflating the success rate.
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
        harness.Repository.BeforeSecondAppendMessages = () => harness.Session.Cancel();
        harness.Repository.SecondAppendMessagesFailure = new OperationCanceledException();

        await harness.Session.SendAsync("hei");

        AssertSingleResult(harness, MetricConstants.MetricsResultCancelledLabelValue);
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

    // Not the cancellation bucket, which has no Failure series for an alert to read.
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

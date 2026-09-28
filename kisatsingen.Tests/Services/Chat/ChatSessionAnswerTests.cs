using System.Runtime.CompilerServices;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// What a turn keeps, on screen and in storage, however it ends. People press
// stop when they have what they need, so an answer must survive it.
public sealed class ChatSessionAnswerTests
{
    [Fact]
    public async Task A_stopped_turn_keeps_what_the_model_had_already_said()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal("halvferdig svar", TurnText.Of(harness.StoredTurn));
    }

    [Fact]
    public async Task A_stopped_turn_stays_on_screen()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal("halvferdig svar", TurnText.Of(harness.VisibleTurn));
    }

    // Through the real fold, from a streamed UsageContent rather than a property set by hand.
    [Fact]
    public async Task A_stopped_turn_stores_the_usage_the_provider_had_already_reported()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringWithUsageThenStalling("halvferdig svar", 900, 100, reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;

        Assert.Equal(new MessageUsage(900, 100, 1000), harness.StoredTurn.Metadata?.Usage);
    }

    [Fact]
    public async Task A_failed_turn_keeps_what_the_model_had_already_said()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("halvferdig svar", new InvalidOperationException("provider gave up"));

        await harness.Session.SendAsync("hei");

        Assert.Equal("halvferdig svar", TurnText.Of(harness.StoredTurn));
    }

    [Fact]
    public async Task A_stopped_turn_records_the_model_key_it_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.StoredTurn.ModelKey);
    }

    // The question is stored before the model is asked, so a crash mid-answer
    // still leaves the row that says the turn never finished.
    [Fact]
    public async Task The_question_is_stored_as_running_before_the_model_answers()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        var storedMidAnswer = harness.StoredTurn;
        harness.Session.Cancel();
        await sending;

        Assert.Equal(("hei", TurnStatus.Running), (storedMidAnswer.Prompt, storedMidAnswer.Status));
    }

    [Fact]
    public async Task A_tool_the_model_used_is_shown_between_the_text_around_it()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.UsingATool("Sjekker klokka.", "Den er tolv.");

        await harness.Session.SendAsync("hva er klokka?");

        Assert.Collection(
            harness.VisibleTurn.Answer,
            segment => Assert.Equal("Sjekker klokka.", Assert.IsType<TextSegment>(segment).Text),
            segment => Assert.Equal(ToolStatus.Completed, Assert.IsType<ToolSegment>(segment).Status),
            segment => Assert.Equal("Den er tolv.", Assert.IsType<TextSegment>(segment).Text));
    }

    // The point of the live view: the call is on the page while the tool runs,
    // not only once the whole answer is in.
    [Fact]
    public async Task A_tool_call_is_shown_as_running_while_the_tool_runs()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.CallingAToolThenStalling(reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        var shownMidTurn = harness.Session.Transcript.Single();
        harness.Session.Cancel();
        await sending;

        Assert.True(shownMidTurn.IsLive);
        Assert.Equal(ToolStatus.Running, Assert.IsType<ToolSegment>(Assert.Single(shownMidTurn.Turn.Answer)).Status);
    }

    [Fact]
    public async Task Each_tool_event_re_renders_the_page()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.UsingATool("før", "etter");
        var shownAnswers = new List<IReadOnlyList<TurnSegment>>();
        harness.Session.StateChanged += () =>
        {
            if (harness.Session.Transcript is [.., var last])
            {
                shownAnswers.Add(last.Turn.Answer);
            }
        };

        await harness.Session.SendAsync("hei");

        Assert.Contains(shownAnswers, answer => answer is [TextSegment, ToolSegment { Status: ToolStatus.Running }]);
        Assert.Contains(shownAnswers, answer => answer is [TextSegment, ToolSegment { Status: ToolStatus.Completed }]);
    }

    // Kept for the reader, though it is never sent back: see TranscriptRequest.
    [Fact]
    public async Task A_turn_stopped_during_a_tool_call_keeps_the_call_as_interrupted()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.CallingAToolThenStalling(reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;

        Assert.Equal(ToolStatus.Interrupted, Assert.IsType<ToolSegment>(Assert.Single(harness.StoredTurn.Answer)).Status);
    }

    // Proves the pruning rule is wired into the request, not just tested beside it.
    [Fact]
    public async Task A_call_interrupted_in_one_turn_is_not_sent_with_the_next()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.CallingAToolThenStalling(reached, ct);
        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;
        harness.Client.OnStream = _ => ModelStream.Answering("svar");

        await harness.Session.SendAsync("igjen");

        Assert.DoesNotContain(harness.Client.LastMessages, message => message.Contents.OfType<FunctionCallContent>().Any());
    }

    [Fact]
    public async Task A_reasoning_chunk_is_counted_rather_than_kept()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => Reasoning();

        await harness.Session.SendAsync("hei");

        Assert.Single(harness.Metrics.Named("_UnkeptContent"));
        Assert.Equal("svar", TurnText.Of(harness.StoredTurn));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Reasoning([EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("tenker")]);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "svar");
        await Task.CompletedTask;
    }

    private static async Task StopAfterAnswering(ChatSessionHarness harness, string text)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling(text, reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;
    }
}

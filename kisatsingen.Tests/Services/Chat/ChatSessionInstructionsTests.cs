using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The prompt reaches the provider and the database by separate paths. A recorded
// prompt that differs from the one sent is worse than none: it reads as evidence.
public sealed class ChatSessionInstructionsTests
{
    [Fact]
    public async Task The_instructions_sent_to_the_model_are_the_ones_stored_with_the_turn()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        var instructionsSent = harness.Client.LastOptions?.Instructions;

        // Two nulls would also pass the comparison below.
        Assert.NotNull(instructionsSent);
        Assert.Equal(instructionsSent, harness.StoredTurn.SystemPrompt);
    }

    [Fact]
    public async Task A_chat_without_an_assistant_is_sent_the_plain_chat_instructions()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Equal(ChatInstructions.ForPlainChat(harness.Today), harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task The_date_in_the_instructions_is_the_norwegian_date_also_when_utc_is_still_on_yesterday()
    {
        await using var harness = new ChatSessionHarness();
        harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero);

        await harness.Session.SendAsync("hei");

        Assert.EndsWith("Today is Sunday 22 November 2026.", harness.Client.LastOptions?.Instructions);
    }

    // A chat that goes on past midnight is told the new date, not the one it started on.
    [Fact]
    public async Task A_message_sent_on_a_later_day_is_given_that_days_date()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("hei");
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddDays(1);

        await harness.Session.SendAsync("hei igjen");

        Assert.EndsWith("Today is Monday 23 November 2026.", harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task The_system_prompt_is_not_also_sent_as_a_message()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(harness.Client.LastMessages, message => message.Role == ChatRole.System);
    }
}

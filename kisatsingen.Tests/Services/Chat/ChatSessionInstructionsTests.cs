using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The prompt reaches the provider and the database by separate paths. A recorded
// prompt that differs from the one sent is worse than none: it reads as evidence.
public sealed class ChatSessionInstructionsTests
{
    [Fact]
    public async Task The_instructions_sent_to_the_model_are_the_ones_persisted_with_the_turn()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        var instructionsSent = harness.Client.LastOptions?.Instructions;
        var instructionsStored = harness.Repository.AppendedMessages
            .Single(message => message.Role == ChatRole.User.Value)
            .SystemPromptSnapshot;

        // Two nulls would also pass the comparison below.
        Assert.NotNull(instructionsSent);
        Assert.Equal(instructionsSent, instructionsStored);
    }

    [Fact]
    public async Task The_system_prompt_is_not_also_sent_as_a_message()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(harness.Client.LastMessages, message => message.Role == ChatRole.System);
    }

    // Losing the tools while cloning would cost capability silently, not fail.
    [Fact]
    public async Task Cloning_the_options_per_turn_keeps_the_tools()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.NotEmpty(harness.Client.LastOptions?.Tools ?? []);
    }
}

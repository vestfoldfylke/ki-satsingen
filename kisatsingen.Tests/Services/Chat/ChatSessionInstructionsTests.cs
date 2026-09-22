using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The system prompt reaches the provider as ChatOptions.Instructions and reaches
// the database as ChatMessage.SystemPromptSnapshot, by two entirely separate
// paths. These tests are what keeps those two from drifting — a transcript whose
// recorded instructions are not the ones the model was given is worse than no
// record at all, because it reads as evidence.
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

        // Asserted non-null first: both being absent would satisfy the comparison
        // below while proving nothing.
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

    // Tools are what make this turn able to answer questions the model cannot on
    // its own. Cloning the options per turn is new, and dropping the tool list on
    // the way through would be a silent loss of capability rather than a failure.
    [Fact]
    public async Task Cloning_the_options_per_turn_keeps_the_tools()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.NotEmpty(harness.Client.LastOptions?.Tools ?? []);
    }
}

using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatInstructionsTests
{
    [Fact]
    public void An_assistants_instructions_follow_the_base_under_a_heading_with_its_name()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.");

        Assert.Equal(
            baseInstructions + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt.",
            instructions);
    }

    [Fact]
    public void Plain_chat_adds_its_instructions_after_the_base_an_assistant_gets()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null);

        Assert.StartsWith(baseInstructions + "\n\n", ChatInstructions.ForPlainChat);
    }
}

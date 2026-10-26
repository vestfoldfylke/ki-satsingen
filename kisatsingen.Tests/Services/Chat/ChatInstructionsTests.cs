using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatInstructionsTests
{
    [Fact]
    public void An_assistants_instructions_follow_the_base_under_a_heading_with_its_name()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.");

        Assert.EndsWith("\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt.", instructions);
    }

    [Fact]
    public void An_assistant_keeps_the_rules_that_apply_to_every_chat()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.");

        Assert.StartsWith("You are vfKI, the AI assistant of Vestfold fylkeskommune", instructions);
    }

    [Fact]
    public void An_assistant_without_instructions_gets_the_base_alone()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", null);

        Assert.DoesNotContain("## Instructions for", instructions);
    }

    [Fact]
    public void An_assistant_gets_no_plain_chat_instructions()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.");

        Assert.DoesNotContain("## Instructions for this chat", instructions);
    }
}

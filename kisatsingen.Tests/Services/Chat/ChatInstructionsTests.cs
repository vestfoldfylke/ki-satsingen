using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatInstructionsTests
{
    [Fact]
    public void An_assistants_instructions_follow_the_base_under_a_heading_with_its_name()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, hasFiles: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", hasFiles: false);

        Assert.Equal(
            baseInstructions + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt.",
            instructions);
    }

    [Fact]
    public void An_assistant_with_files_is_told_so_after_its_instructions()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, hasFiles: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", hasFiles: true);

        Assert.Equal(
            baseInstructions + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt.\n\n" + ChatInstructions.AssistantFilesLine,
            instructions);
    }

    [Fact]
    public void An_assistant_with_files_but_no_instructions_gets_the_heading_and_the_files_line()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, hasFiles: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", null, hasFiles: true);

        Assert.Equal(
            baseInstructions + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\n" + ChatInstructions.AssistantFilesLine,
            instructions);
    }

    [Fact]
    public void Plain_chat_adds_its_instructions_after_the_base_an_assistant_gets()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, hasFiles: false);

        Assert.StartsWith(baseInstructions + "\n\n", ChatInstructions.ForPlainChat);
    }
}

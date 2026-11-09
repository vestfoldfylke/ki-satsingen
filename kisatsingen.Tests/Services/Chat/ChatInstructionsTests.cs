using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatInstructionsTests
{
    private static readonly KnowledgeFileMetadata Circular = File("rundskriv.md", Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly KnowledgeFileMetadata Budget = File("budsjett.md", Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void An_assistants_instructions_follow_the_base_under_a_heading_with_its_name()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [], areBaseInstructionsDisabled: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [], areBaseInstructionsDisabled: false);

        Assert.Equal(
            baseInstructions + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt.",
            instructions);
    }

    [Fact]
    public void An_assistants_files_are_listed_by_name_and_id_in_a_section_after_its_instructions()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [], areBaseInstructionsDisabled: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [Circular, Budget], areBaseInstructionsDisabled: false);

        Assert.Equal(
            baseInstructions +
            "\n\n## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt." +
            "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)" +
            "\n- budsjett.md (fileId: 22222222-2222-2222-2222-222222222222)",
            instructions);
    }

    [Fact]
    public void An_assistant_with_files_but_no_instructions_gets_only_the_files_section()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [], areBaseInstructionsDisabled: false);

        var instructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [Circular], areBaseInstructionsDisabled: false);

        Assert.Equal(
            baseInstructions + "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)",
            instructions);
    }

    [Fact]
    public void An_assistant_with_the_base_instructions_disabled_starts_at_its_own_instructions()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [Circular], areBaseInstructionsDisabled: true);

        Assert.Equal(
            "## Instructions for the assistant \"Saksbehandler\"\n\nSvar kort og formelt." +
            "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)",
            instructions);
    }

    [Fact]
    public void Plain_chat_adds_its_instructions_after_the_base_an_assistant_gets()
    {
        var baseInstructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [], areBaseInstructionsDisabled: false);

        Assert.StartsWith(baseInstructions + "\n\n", ChatInstructions.ForPlainChat);
    }

    private static KnowledgeFileMetadata File(string fileName, Guid id) => new(
        id,
        fileName,
        "text/markdown",
        SizeBytes: 1,
        Sha256: new string('a', 64),
        Summary: "Utdrag.",
        LineCount: 1,
        EstimatedTokenCount: 1,
        ContentOrigin.TextFile,
        PageCount: null,
        Version: 1,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
}

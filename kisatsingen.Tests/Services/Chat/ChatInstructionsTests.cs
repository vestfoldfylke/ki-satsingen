using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatInstructionsTests
{
    private static readonly KnowledgeFileMetadata Circular = File("rundskriv.md", Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly KnowledgeFileMetadata Budget = File("budsjett.md", Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static readonly DateOnly Today = new(2026, 11, 22);
    private const string DateSection = "\n\n## Date\n\nToday is Sunday 22 November 2026.";

    [Fact]
    public void An_assistants_instructions_follow_the_base_under_a_heading_with_its_name()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [], areBaseInstructionsDisabled: false, Today);

        Assert.Equal(
            ChatInstructions.Base + "\n\n## Instructions for the assistant \"Saksbehandler\"\n\n" + Wrapped("Svar kort og formelt.") + DateSection,
            instructions);
    }

    [Fact]
    public void An_assistants_files_are_listed_by_name_and_id_in_a_section_after_its_instructions()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [Circular, Budget], areBaseInstructionsDisabled: false, Today);

        Assert.Equal(
            ChatInstructions.Base +
            "\n\n## Instructions for the assistant \"Saksbehandler\"\n\n" + Wrapped("Svar kort og formelt.") +
            "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)" +
            "\n- budsjett.md (fileId: 22222222-2222-2222-2222-222222222222)" +
            DateSection,
            instructions);
    }

    [Fact]
    public void An_assistant_with_files_but_no_instructions_gets_only_the_files_section()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", null, files: [Circular], areBaseInstructionsDisabled: false, Today);

        Assert.Equal(
            ChatInstructions.Base + "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)" +
            DateSection,
            instructions);
    }

    [Fact]
    public void An_assistant_with_the_base_instructions_disabled_starts_at_its_own_instructions_and_still_gets_the_date()
    {
        var instructions = ChatInstructions.ForAssistant("Saksbehandler", "Svar kort og formelt.", files: [Circular], areBaseInstructionsDisabled: true, Today);

        Assert.Equal(
            "## Instructions for the assistant \"Saksbehandler\"\n\n" + Wrapped("Svar kort og formelt.") +
            "\n\n## Files\n\n" + ChatInstructions.AssistantFilesLine +
            "\n\n- rundskriv.md (fileId: 11111111-1111-1111-1111-111111111111)" +
            DateSection,
            instructions);
    }

    // Headings of the author's own would otherwise sit level with the base sections.
    [Fact]
    public void An_assistants_own_headings_stay_inside_the_tags_around_its_instructions()
    {
        var instructions = ChatInstructions.ForAssistant("Assistenthjelperen", "## Rolle\nDu hjelper.\n\n## Filer\nBruk få filer.", files: [], areBaseInstructionsDisabled: true, Today);

        Assert.Equal(
            "## Instructions for the assistant \"Assistenthjelperen\"\n\n" +
            "<assistant_instructions>\n## Rolle\nDu hjelper.\n\n## Filer\nBruk få filer.\n</assistant_instructions>" +
            DateSection,
            instructions);
    }

    [Fact]
    public void Plain_chat_adds_its_instructions_after_the_base_and_ends_with_the_date()
    {
        var instructions = ChatInstructions.ForPlainChat(Today);

        Assert.True(instructions.StartsWith(ChatInstructions.Base + "\n\n## Instructions\n", StringComparison.Ordinal) && instructions.EndsWith(DateSection, StringComparison.Ordinal));
    }

    private static string Wrapped(string instructions) => $"<assistant_instructions>\n{instructions}\n</assistant_instructions>";

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

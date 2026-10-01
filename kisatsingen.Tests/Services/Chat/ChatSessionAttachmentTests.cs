using kisatsingen.Tests.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.KnowledgeFiles.Processing;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.AI;
using Xunit;
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionAttachmentTests
{
    private static readonly string[] FileToolNames = ["list_files", "get_outline", "read_file"];

    private static byte[] MarkdownBytes(string text) => System.Text.Encoding.UTF8.GetBytes($"# {text}\n\nInnhold.\n");

    private static Task AttachAsync(ChatSessionHarness harness, params (string Name, byte[] Content)[] files) =>
        harness.Attachments.UploadAsync(new InputFileChangeEventArgs([.. files.Select(file => new FakeBrowserFile(file.Name, file.Content))]));

    private static string LastUserMessage(ChatSessionHarness harness) =>
        harness.Client.LastMessages.Last(message => message.Role == ChatRole.User).Text;

    private static Guid SavedFileId(ChatSessionHarness harness, string fileName) =>
        harness.KnowledgeFiles.Saved.Single(file => file.FileName == fileName).Id;

    private static IEnumerable<string> ToolNames(ChatSessionHarness harness) =>
        harness.Client.LastOptions!.Tools!.Select(tool => tool.Name);

    [Fact]
    public async Task Opening_another_chat_discards_what_was_attached_in_this_one()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", UploadBytes.Text));

        await harness.Session.LoadAsync(Guid.NewGuid());

        Assert.Empty(harness.Attachments.Pending);
        Assert.Empty(harness.AttachmentEnvironment.FilesOnDisk);
    }

    [Fact]
    public async Task The_user_message_names_each_saved_file_by_its_id_and_says_to_read_it_first()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));

        await harness.Session.SendAsync("Hva står i notatet?");

        var message = LastUserMessage(harness);
        Assert.StartsWith("Hva står i notatet?", message);
        Assert.Contains(AttachmentTexts.AvailableEntry("notat.md", SavedFileId(harness, "notat.md")), message);
        Assert.Contains(AttachmentTexts.ReadBeforeAnswering, message);
    }

    [Fact]
    public async Task The_attachments_are_stored_with_the_turn()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));

        await harness.Session.SendAsync("Hva står i notatet?");

        var stored = Assert.Single(harness.StoredTurn.Attachments);
        Assert.Equal(new TurnAttachment("notat.md", SavedFileId(harness, "notat.md")), stored);
    }

    [Fact]
    public async Task A_file_that_cannot_be_processed_is_named_as_unavailable_with_its_reason_and_the_turn_still_answers()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("binær.txt", [0x00, 0x01, 0x02, 0x03, 0x00, 0x00]));

        await harness.Session.SendAsync("Hva står i filen?");

        Assert.Contains(AttachmentTexts.UnavailableEntry("binær.txt", ConversionRejections.Binary), LastUserMessage(harness));
        Assert.Equal(TurnStatus.Completed, harness.VisibleTurn.Status);
    }

    [Fact]
    public async Task A_file_whose_save_fails_is_named_as_unavailable_while_the_others_are_saved()
    {
        await using var harness = new ChatSessionHarness();
        harness.KnowledgeFiles.FailSavingOf.Add("tapt.md");
        await AttachAsync(harness, ("tapt.md", MarkdownBytes("Tapt")), ("lagret.md", MarkdownBytes("Lagret")));

        await harness.Session.SendAsync("Hva står i filene?");

        var message = LastUserMessage(harness);
        Assert.Contains(AttachmentTexts.UnavailableEntry("tapt.md", AttachmentTexts.NotSavedReason), message);
        Assert.Contains(AttachmentTexts.AvailableEntry("lagret.md", SavedFileId(harness, "lagret.md")), message);
        Assert.Equal(TurnStatus.Completed, harness.VisibleTurn.Status);
    }

    [Fact]
    public async Task Files_finishing_out_of_order_are_each_saved_once_and_listed_in_the_order_they_were_attached()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("først.md");
        await AttachAsync(harness, ("først.md", MarkdownBytes("Først")), ("sist.md", MarkdownBytes("Sist")));

        var send = harness.Session.SendAsync("Hva står i filene?");
        await Eventually.TrueAsync(() => harness.KnowledgeFiles.Saved.Count == 1, "The file that was not held was never saved.");
        harness.TextConverter.Release("først.md");
        await send;

        Assert.Equal(["sist.md", "først.md"], harness.KnowledgeFiles.Saved.Select(file => file.FileName));
        Assert.Equal(["først.md", "sist.md"], harness.StoredTurn.Attachments.Select(attachment => attachment.FileName));
        Assert.All(harness.StoredTurn.Attachments, attachment => Assert.NotNull(attachment.FileId));
    }

    [Fact]
    public async Task A_stop_during_processing_keeps_the_file_already_saved_and_leaves_out_the_unfinished_one()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("treg.md");
        await AttachAsync(harness, ("treg.md", MarkdownBytes("Treg")), ("rask.md", MarkdownBytes("Rask")));

        var send = harness.Session.SendAsync("Hva står i filene?");
        await harness.TextConverter.WaitUntilStartedAsync("treg.md");
        await Eventually.TrueAsync(() => harness.KnowledgeFiles.Saved.Count == 1, "The file that was not held was never saved.");
        harness.Session.Cancel();
        await send;

        Assert.Equal(TurnStatus.Stopped, harness.StoredTurn.Status);
        var kept = Assert.Single(harness.StoredTurn.Attachments);
        Assert.Equal(new TurnAttachment("rask.md", SavedFileId(harness, "rask.md")), kept);
    }

    [Fact]
    public async Task A_stop_before_the_attachments_are_taken_leaves_them_in_the_composer()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));
        harness.Repository.BeforeCreateChat = () =>
        {
            harness.Session.Cancel();
            return Task.CompletedTask;
        };

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(TurnStatus.Stopped, harness.VisibleTurn.Status);
        var pending = Assert.Single(harness.Attachments.Pending);
        Assert.Equal(AttachmentStatus.Ready, pending.Status);
    }

    // Once taken, the turn owns the temp files; a file it never handed to
    // processing would stay on disk until the age sweep.
    [Fact]
    public async Task No_temp_file_is_left_on_disk_after_a_turn_stopped_during_processing()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("treg.md");
        await AttachAsync(harness, ("treg.md", MarkdownBytes("Treg")), ("rask.md", MarkdownBytes("Rask")));

        var send = harness.Session.SendAsync("Hva står i filene?");
        await harness.TextConverter.WaitUntilStartedAsync("treg.md");
        harness.Session.Cancel();
        await send;

        await Eventually.TrueAsync(() => harness.AttachmentEnvironment.FilesOnDisk.Count == 0, "A temp file was left on disk after the turn ended.");
    }

    [Fact]
    public async Task The_attachments_show_as_processing_while_they_are_processed()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("notat.md");
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));

        var send = harness.Session.SendAsync("Hva står i notatet?");
        await harness.TextConverter.WaitUntilStartedAsync("notat.md");
        var shownWhileProcessing = harness.VisibleTurn.Attachments;
        harness.TextConverter.Release("notat.md");
        await send;

        Assert.Equal([TurnAttachment.Processing("notat.md")], shownWhileProcessing);
    }

    [Fact]
    public async Task Attachments_stay_in_the_composer_when_the_message_could_not_be_saved()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("Database down.");
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));

        await harness.Session.SendAsync("Hva står i notatet?");

        var pending = Assert.Single(harness.Attachments.Pending);
        Assert.Equal(AttachmentStatus.Ready, pending.Status);
    }

    [Fact]
    public async Task A_file_that_finishes_uploading_after_send_stays_for_the_next_message()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("sendt.md", MarkdownBytes("Sendt")));
        harness.Repository.BeforeCreateChat = () => AttachAsync(harness, ("senere.md", MarkdownBytes("Senere")));

        await harness.Session.SendAsync("Hva står i filen?");

        Assert.Equal(["sendt.md"], harness.StoredTurn.Attachments.Select(attachment => attachment.FileName));
        Assert.Equal(["senere.md"], harness.Attachments.Pending.Select(attachment => attachment.FileName));
    }

    [Fact]
    public async Task Rejected_attachments_are_cleared_on_send()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("program.exe", [0x4D, 0x5A]));

        await harness.Session.SendAsync("Hei");

        Assert.Empty(harness.Attachments.Pending);
    }

    [Fact]
    public async Task The_attachment_line_is_replayed_unchanged_even_after_the_file_is_deleted()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));
        await harness.Session.SendAsync("Hva står i notatet?");
        var firstMessage = LastUserMessage(harness);
        harness.KnowledgeFiles.Delete(SavedFileId(harness, "notat.md"));

        await harness.Session.SendAsync("Og så?");

        var replayed = harness.Client.LastMessages.First(message => message.Role == ChatRole.User).Text;
        Assert.Equal(firstMessage, replayed);
    }

    [Fact]
    public async Task A_chat_without_files_gets_no_file_tools()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("Hei");

        Assert.Empty(ToolNames(harness).Intersect(FileToolNames));
    }

    [Fact]
    public async Task The_first_attached_file_brings_the_file_tools_in_that_same_turn()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(FileToolNames, ToolNames(harness).Intersect(FileToolNames));
    }

    // Scoping inside the tools is tested in FileToolsTests; this is the wiring:
    // tools bound to another owner or chat would list nothing.
    [Fact]
    public async Task The_file_tools_the_model_gets_read_this_turns_owner_and_chat()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));
        await harness.Session.SendAsync("Hva står i notatet?");
        var listFiles = harness.Client.LastOptions!.Tools!.OfType<AIFunction>().Single(tool => tool.Name == "list_files");

        var result = await listFiles.InvokeAsync(new AIFunctionArguments());

        Assert.Contains("\"name\":\"notat.md\"", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task A_file_saved_in_an_earlier_turn_keeps_the_file_tools_in_the_next_one()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, ("notat.md", MarkdownBytes("Notat")));
        await harness.Session.SendAsync("Hva står i notatet?");

        await harness.Session.SendAsync("Og så?");

        Assert.Equal(FileToolNames, ToolNames(harness).Intersect(FileToolNames));
    }

    [Fact]
    public async Task A_chat_that_already_has_files_gets_the_file_tools_without_a_new_attachment()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "med filer",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.Draft("notat.md", "# Notat\n"));
        await harness.Session.LoadAsync(chat.Id);

        await harness.Session.SendAsync("Hva sto i notatet?");

        Assert.Equal(FileToolNames, ToolNames(harness).Intersect(FileToolNames));
    }
}

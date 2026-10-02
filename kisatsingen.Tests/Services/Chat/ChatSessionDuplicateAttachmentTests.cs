using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.KnowledgeFiles.Processing;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The same content attached again: in the same composer it is refused, and
// once saved in the chat it is available as that file instead of saved twice.
public sealed class ChatSessionDuplicateAttachmentTests
{
    private static readonly byte[] NoteBytes = System.Text.Encoding.UTF8.GetBytes("# Notat\n\nInnhold.\n");
    private static readonly byte[] OtherBytes = System.Text.Encoding.UTF8.GetBytes("# Annet\n\nAnnet innhold.\n");

    // Well under the fake's hold on the listing, so the turn must end because
    // of the stop, not because the lookup gave up.
    private static readonly TimeSpan PromptStop = TimeSpan.FromSeconds(2);

    private static Task AttachAsync(ChatSessionHarness harness, params (string Name, byte[] Content)[] files) =>
        harness.Attachments.UploadAsync(new InputFileChangeEventArgs([.. files.Select(file => new FakeBrowserFile(file.Name, file.Content))]));

    private static string LastUserMessage(ChatSessionHarness harness) =>
        harness.Client.LastMessages.Last(message => message.Role == ChatRole.User).Text;

    private static IReadOnlyList<TurnAttachment> LatestTurnAttachments(ChatSessionHarness harness) =>
        harness.Session.Transcript[^1].Turn.Attachments;

    private static async Task<Guid> SendWithSavedNoteAsync(ChatSessionHarness harness)
    {
        await AttachAsync(harness, ("notat.md", NoteBytes));
        await harness.Session.SendAsync("Hva står i notatet?");
        return Assert.Single(harness.KnowledgeFiles.Saved).Id;
    }

    [Fact]
    public async Task The_same_file_twice_in_one_selection_is_refused_the_second_time()
    {
        await using var harness = new ChatSessionHarness();

        await AttachAsync(harness, ("notat.md", NoteBytes), ("notat-kopi.md", NoteBytes));

        var copy = harness.Attachments.Pending.Single(attachment => attachment.FileName == "notat-kopi.md");
        Assert.Equal((AttachmentStatus.Rejected, UploadRejections.AlreadyPending), (copy.Status, copy.RejectionReason));
    }

    [Fact]
    public async Task A_file_already_saved_in_the_chat_is_available_as_that_file_without_a_new_save()
    {
        await using var harness = new ChatSessionHarness();
        var savedFileId = await SendWithSavedNoteAsync(harness);
        await AttachAsync(harness, ("notat-kopi.md", NoteBytes));

        await harness.Session.SendAsync("Se på notatet igjen.");

        Assert.Contains(AttachmentTexts.AvailableEntry("notat-kopi.md", savedFileId), LastUserMessage(harness));
        Assert.Single(harness.KnowledgeFiles.Saved);
        await Eventually.TrueAsync(() => harness.AttachmentEnvironment.FilesOnDisk.Count == 0, "The reused file's temp file was left on disk.");
    }

    // Checked against the database at send, so the session's own view of the
    // chat's files cannot be stale.
    [Fact]
    public async Task A_file_saved_elsewhere_since_the_chat_was_opened_is_available_as_that_file()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        var savedElsewhere = harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, harness.Session.ChatId!.Value, "fra-annen-fane.md", NoteBytes);
        await AttachAsync(harness, ("notat.md", NoteBytes));

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Contains(AttachmentTexts.AvailableEntry("notat.md", savedElsewhere.Id), LastUserMessage(harness));
        Assert.Single(harness.KnowledgeFiles.Saved);
    }

    [Fact]
    public async Task A_failed_lookup_processes_the_files_as_new_and_the_turn_still_answers()
    {
        await using var harness = new ChatSessionHarness();
        harness.KnowledgeFiles.FailListing = true;
        await AttachAsync(harness, ("notat.md", NoteBytes));

        await harness.Session.SendAsync("Hva står i notatet?");

        var saved = Assert.Single(harness.KnowledgeFiles.Saved);
        Assert.Contains(AttachmentTexts.AvailableEntry("notat.md", saved.Id), LastUserMessage(harness));
        Assert.Equal(TurnStatus.Completed, harness.VisibleTurn.Status);
    }

    [Fact]
    public async Task A_file_already_saved_is_available_while_the_others_are_still_processing()
    {
        await using var harness = new ChatSessionHarness();
        var savedFileId = await SendWithSavedNoteAsync(harness);
        harness.TextConverter.Hold("ny.md");
        await AttachAsync(harness, ("notat-kopi.md", NoteBytes), ("ny.md", OtherBytes));

        var send = harness.Session.SendAsync("Se på begge.");
        await harness.TextConverter.WaitUntilStartedAsync("ny.md");
        var shownWhileProcessing = LatestTurnAttachments(harness);
        harness.TextConverter.Release("ny.md");
        await send;

        Assert.Equal([new TurnAttachment("notat-kopi.md", savedFileId), TurnAttachment.Processing("ny.md")], shownWhileProcessing);
    }

    [Fact]
    public async Task The_attachments_are_shown_as_processing_before_the_lookup_answers()
    {
        await using var harness = new ChatSessionHarness();
        harness.KnowledgeFiles.HoldListing();
        await AttachAsync(harness, ("notat.md", NoteBytes));

        var send = harness.Session.SendAsync("Hva står i notatet?");
        await harness.KnowledgeFiles.WaitUntilListingStartedAsync();
        var shownDuringLookup = harness.VisibleTurn.Attachments;
        harness.KnowledgeFiles.ReleaseListing();
        await send;

        Assert.Equal([TurnAttachment.Processing("notat.md")], shownDuringLookup);
    }

    [Fact]
    public async Task A_stop_during_the_lookup_ends_the_turn_at_once_and_leaves_no_temp_file_behind()
    {
        await using var harness = new ChatSessionHarness();
        harness.KnowledgeFiles.HoldListing();
        await AttachAsync(harness, ("notat.md", NoteBytes));

        var send = harness.Session.SendAsync("Hva står i notatet?");
        await harness.KnowledgeFiles.WaitUntilListingStartedAsync();
        harness.Session.Cancel();
        await send.WaitAsync(PromptStop);

        Assert.Empty(harness.KnowledgeFiles.Saved);
        await Eventually.TrueAsync(() => harness.AttachmentEnvironment.FilesOnDisk.Count == 0, "A temp file was left on disk after a stop during the lookup.");
    }

    [Fact]
    public async Task Two_saved_files_with_the_same_content_reuse_the_first_rather_than_failing_the_lookup()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        var chatId = harness.Session.ChatId!.Value;
        var first = harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, chatId, "første.md", NoteBytes);
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, chatId, "andre.md", NoteBytes);
        await AttachAsync(harness, ("notat.md", NoteBytes));

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Contains(AttachmentTexts.AvailableEntry("notat.md", first.Id), LastUserMessage(harness));
    }
}

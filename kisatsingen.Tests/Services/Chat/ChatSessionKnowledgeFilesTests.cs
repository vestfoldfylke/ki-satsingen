using kisatsingen.Tests.Data.Repositories;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.KnowledgeFiles.Processing;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.AI;
using Xunit;
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// The chat's knowledge files as the session knows them: loaded with the chat,
// and added to as a turn makes each file available.
public sealed class ChatSessionKnowledgeFilesTests
{
    private static readonly byte[] NoteBytes = System.Text.Encoding.UTF8.GetBytes("# Notat\n\nInnhold.\n");

    private static Task AttachAsync(ChatSessionHarness harness, string fileName) =>
        AttachAsync(harness, (fileName, NoteBytes));

    private static Task AttachAsync(ChatSessionHarness harness, params (string Name, byte[] Content)[] files) =>
        harness.Attachments.UploadAsync(new InputFileChangeEventArgs([.. files.Select(file => new FakeBrowserFile(file.Name, file.Content))]));

    private static byte[] MarkdownBytes(string text) => System.Text.Encoding.UTF8.GetBytes($"# {text}\n\nInnhold.\n");

    private static ChatEntity StoreChat(ChatSessionHarness harness) => harness.Repository.Store(new ChatEntity
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Title = "lagret",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });

    private static IEnumerable<string> KnowledgeFileNames(ChatSessionHarness harness) =>
        harness.Session.KnowledgeFiles.Select(file => file.FileName);

    [Fact]
    public async Task Opening_a_chat_lists_its_knowledge_files()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.Draft("notat.md", "# Notat\n"));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(["notat.md"], KnowledgeFileNames(harness));
    }

    [Fact]
    public async Task Opening_a_new_chat_empties_the_list()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.Draft("notat.md", "# Notat\n"));
        await harness.Session.LoadAsync(chat.Id);

        await harness.Session.LoadAsync(null);

        Assert.Empty(harness.Session.KnowledgeFiles);
    }

    [Fact]
    public async Task A_turn_that_saved_a_file_adds_it_to_the_list()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, "notat.md");

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(["notat.md"], KnowledgeFileNames(harness));
    }

    [Fact]
    public async Task A_saved_file_is_listed_while_the_turn_is_still_answering()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, "notat.md");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig svar", reached, ct);

        var sending = harness.Session.SendAsync("Hva står i notatet?");
        await reached.Task;

        Assert.Equal(["notat.md"], KnowledgeFileNames(harness));
        harness.Session.Cancel();
        await sending;
    }

    // The second turn reuses the file the first one saved, and reports it again.
    [Fact]
    public async Task A_file_attached_again_in_a_later_turn_is_listed_once()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, "notat.md");
        await harness.Session.SendAsync("Hva står i notatet?");
        await AttachAsync(harness, "notat-kopi.md");

        await harness.Session.SendAsync("Se på notatet igjen.");

        Assert.Equal(["notat.md"], KnowledgeFileNames(harness));
    }

    // The turn reuses the copy another tab saved, so nothing new is saved for
    // the list to learn of.
    [Fact]
    public async Task A_file_another_tab_saved_is_listed_once_a_turn_reuses_it()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, harness.Session.ChatId!.Value, "fra-annen-fane.md", NoteBytes);
        await AttachAsync(harness, "notat.md");

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(["fra-annen-fane.md"], KnowledgeFileNames(harness));
    }

    [Fact]
    public async Task After_a_turn_reused_a_file_another_tab_saved_the_next_turn_gets_the_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, harness.Session.ChatId!.Value, "fra-annen-fane.md", NoteBytes);
        await AttachAsync(harness, "notat.md");
        await harness.Session.SendAsync("Hva står i notatet?");

        await harness.Session.SendAsync("Og så?");

        Assert.Contains("read_file", harness.Client.LastOptions!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task A_stop_during_processing_keeps_the_file_already_saved_in_the_list()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("treg.md");
        await AttachAsync(harness, ("treg.md", MarkdownBytes("Treg")), ("rask.md", MarkdownBytes("Rask")));

        var sending = harness.Session.SendAsync("Hva står i filene?");
        await harness.TextConverter.WaitUntilStartedAsync("treg.md");
        await Eventually.TrueAsync(() => harness.KnowledgeFiles.Saved.Count == 1, "The file that was not held was never saved.");
        harness.Session.Cancel();
        await sending;

        Assert.Equal(["rask.md"], KnowledgeFileNames(harness));
    }

    // Reset is what deleting the open chat does: it moves the view on without
    // stopping the turn, so the file is saved after the view stopped being the
    // turn's.
    [Fact]
    public async Task A_file_saved_after_the_view_moved_on_stays_out_of_the_list()
    {
        await using var harness = new ChatSessionHarness();
        harness.TextConverter.Hold("notat.md");
        await AttachAsync(harness, "notat.md");
        var sending = harness.Session.SendAsync("Hva står i notatet?");
        await harness.TextConverter.WaitUntilStartedAsync("notat.md");

        harness.Session.Reset();
        harness.TextConverter.Release("notat.md");
        await sending;

        Assert.Single(harness.KnowledgeFiles.Saved);
        Assert.Empty(harness.Session.KnowledgeFiles);
    }
}

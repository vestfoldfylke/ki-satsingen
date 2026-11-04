using kisatsingen.Data.Entities;
using kisatsingen.Tests.Data.Repositories;
using kisatsingen.Tests.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// The chat's knowledge files as the session knows them: loaded with the chat,
// added to as a turn makes each file available, and removed when deleted.
public sealed class ChatSessionKnowledgeFilesTests
{
    private static readonly byte[] NoteBytes = System.Text.Encoding.UTF8.GetBytes("# Notat\n\nInnhold.\n");

    private static Task AttachAsync(ChatSessionHarness harness, string fileName) =>
        AttachAsync(harness, (fileName, NoteBytes));

    private static Task AttachAsync(ChatSessionHarness harness, params (string Name, byte[] Content)[] files) =>
        harness.Attachments.AttachAsync(new InputFileChangeEventArgs([.. files.Select(file => new FakeBrowserFile(file.Name, file.Content))]));

    private static byte[] MarkdownBytes(string text) => System.Text.Encoding.UTF8.GetBytes($"# {text}\n\nInnhold.\n");

    private static ChatEntity StoreChat(ChatSessionHarness harness) => harness.Repository.Store(new ChatEntity
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Title = "lagret",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });

    private static IEnumerable<string> ChatFileNames(ChatSessionHarness harness) =>
        harness.Session.ChatFiles.Select(file => file.FileName);

    [Fact]
    public async Task Opening_a_chat_lists_its_knowledge_files()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.NewFile("notat.md", "# Notat\n"));

        await harness.Session.OpenChatAsync(chat.Id);

        Assert.Equal(["notat.md"], ChatFileNames(harness));
    }

    [Fact]
    public async Task Starting_a_new_chat_empties_the_list()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.NewFile("notat.md", "# Notat\n"));
        await harness.Session.OpenChatAsync(chat.Id);

        await harness.Session.StartNewPlainChatAsync();

        Assert.Empty(harness.Session.ChatFiles);
    }

    [Fact]
    public async Task A_turn_that_saved_a_file_adds_it_to_the_list()
    {
        await using var harness = new ChatSessionHarness();
        await AttachAsync(harness, "notat.md");

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(["notat.md"], ChatFileNames(harness));
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

        Assert.Equal(["notat.md"], ChatFileNames(harness));
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

        Assert.Equal(["notat.md"], ChatFileNames(harness));
    }

    // The turn reuses the copy another tab saved, so nothing new is saved for
    // the list to learn of.
    [Fact]
    public async Task A_file_another_tab_saved_is_listed_once_a_turn_reuses_it()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, harness.Session.ChatId, "fra-annen-fane.md", NoteBytes);
        await AttachAsync(harness, "notat.md");

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(["fra-annen-fane.md"], ChatFileNames(harness));
    }

    [Fact]
    public async Task After_a_turn_reused_a_file_another_tab_saved_the_next_turn_gets_the_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("Hei");
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, harness.Session.ChatId, "fra-annen-fane.md", NoteBytes);
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
        await AttachAsync(harness, ("rask.md", MarkdownBytes("Rask")), ("treg.md", MarkdownBytes("Treg")));

        var sending = harness.Session.SendAsync("Hva står i filene?");
        await harness.TextConverter.WaitUntilStartedAsync("treg.md");
        harness.Session.Cancel();
        await sending;

        Assert.Equal(["rask.md"], ChatFileNames(harness));
    }

    [Fact]
    public async Task A_deleted_file_is_removed_from_storage_and_the_list()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        var file = SaveIntoChat(harness, chat.Id, "notat.md");
        await harness.Session.OpenChatAsync(chat.Id);

        await harness.Session.DeleteChatFileAsync(chat.Id, file.Id);

        Assert.Empty(harness.Session.ChatFiles);
        Assert.Empty(harness.KnowledgeFiles.Saved);
    }

    [Fact]
    public async Task A_file_is_deleted_from_the_chat_given_after_another_chat_was_opened()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        var file = SaveIntoChat(harness, chat.Id, "notat.md");
        await harness.Session.OpenChatAsync(chat.Id);
        await harness.Session.OpenChatAsync(StoreChat(harness).Id);

        var isDeleted = await harness.Session.DeleteChatFileAsync(chat.Id, file.Id);

        Assert.True(isDeleted);
    }

    [Fact]
    public async Task A_file_already_deleted_elsewhere_leaves_the_list_too()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        var file = SaveIntoChat(harness, chat.Id, "notat.md");
        await harness.Session.OpenChatAsync(chat.Id);
        harness.KnowledgeFiles.Delete(file.Id);

        var isDeleted = await harness.Session.DeleteChatFileAsync(chat.Id, file.Id);

        Assert.False(isDeleted);
        Assert.Empty(harness.Session.ChatFiles);
    }

    [Fact]
    public async Task After_the_last_file_is_deleted_the_next_turn_has_no_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        var file = SaveIntoChat(harness, chat.Id, "notat.md");
        await harness.Session.OpenChatAsync(chat.Id);
        await harness.Session.DeleteChatFileAsync(chat.Id, file.Id);

        await harness.Session.SendAsync("Hei");

        Assert.DoesNotContain("read_file", ToolNames(harness));
    }

    private static KnowledgeFileMetadata SaveIntoChat(ChatSessionHarness harness, Guid chatId, string fileName) =>
        harness.KnowledgeFiles.SaveElsewhere(ChatSessionHarness.OwnerUnderTest, chatId, fileName, NoteBytes);

    private static IEnumerable<string> ToolNames(ChatSessionHarness harness) =>
        harness.Client.LastOptions?.Tools?.Select(tool => tool.Name) ?? [];
}

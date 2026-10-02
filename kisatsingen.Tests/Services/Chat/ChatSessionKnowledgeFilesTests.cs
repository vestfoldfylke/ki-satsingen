using kisatsingen.Tests.Data.Repositories;
using kisatsingen.Tests.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.AI;
using Xunit;
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// The chat's knowledge files as the session knows them: loaded with the chat,
// and reloaded after a turn with an available attachment.
public sealed class ChatSessionKnowledgeFilesTests
{
    private static readonly byte[] NoteBytes = System.Text.Encoding.UTF8.GetBytes("# Notat\n\nInnhold.\n");

    private static Task AttachAsync(ChatSessionHarness harness, string fileName) =>
        harness.Attachments.UploadAsync(new InputFileChangeEventArgs([new FakeBrowserFile(fileName, NoteBytes)]));

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

    // The turn has saved its file and is still answering when it returns.
    private static async Task<(Task Sending, Guid ChatId)> StartTurnWithSavedFileAsync(ChatSessionHarness harness)
    {
        await AttachAsync(harness, "notat.md");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig svar", reached, ct);
        var sending = harness.Session.SendAsync("Hva står i notatet?");
        await reached.Task;
        return (sending, harness.Session.ChatId!.Value);
    }

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
    public async Task A_failed_reload_keeps_the_list_from_before_the_turn()
    {
        await using var harness = new ChatSessionHarness();
        var chat = StoreChat(harness);
        await harness.KnowledgeFiles.CreateFileForChatAsync(ChatSessionHarness.OwnerUnderTest, chat.Id, KnowledgeFileFactory.Draft("gammel.md", "# Gammel\n"));
        await harness.Session.LoadAsync(chat.Id);
        harness.KnowledgeFiles.FailListing = true;
        await AttachAsync(harness, "notat.md");

        await harness.Session.SendAsync("Hva står i notatet?");

        Assert.Equal(["gammel.md"], KnowledgeFileNames(harness));
    }

    // The list never learnt of the file, so only the turn that saved it tells
    // the next one that the chat has files.
    [Fact]
    public async Task After_a_failed_reload_the_next_turn_still_gets_the_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        harness.KnowledgeFiles.FailListing = true;
        await AttachAsync(harness, "notat.md");
        await harness.Session.SendAsync("Hva står i notatet?");

        await harness.Session.SendAsync("Og så?");

        Assert.Contains("read_file", harness.Client.LastOptions!.Tools!.Select(tool => tool.Name));
    }

    // The left chat's listing is held, so a reload that ran anyway would
    // answer after the next chat is shown, as a real query would.
    [Fact]
    public async Task A_turn_ended_by_leaving_the_chat_does_not_reload_into_the_chat_opened_next()
    {
        await using var harness = new ChatSessionHarness();
        var destination = StoreChat(harness);
        var (sending, leftChatId) = await StartTurnWithSavedFileAsync(harness);
        harness.KnowledgeFiles.HoldListing(onlyForChat: leftChatId);

        await harness.Session.LoadAsync(destination.Id);
        harness.KnowledgeFiles.ReleaseListing();
        await sending;

        Assert.Equal(destination.Id, harness.Session.ChatId);
        Assert.Empty(harness.Session.KnowledgeFiles);
    }

    [Fact]
    public async Task A_reload_that_answers_after_the_user_opened_another_chat_is_discarded()
    {
        await using var harness = new ChatSessionHarness();
        var destination = StoreChat(harness);
        var (sending, leftChatId) = await StartTurnWithSavedFileAsync(harness);
        harness.KnowledgeFiles.HoldListing(onlyForChat: leftChatId);
        // A stop ends the turn while the user is still in its chat, so the
        // reload starts; the user leaves only while it is in flight.
        harness.Session.Cancel();
        await harness.KnowledgeFiles.WaitUntilListingStartedAsync();

        await harness.Session.LoadAsync(destination.Id);
        harness.KnowledgeFiles.ReleaseListing();
        await sending;

        Assert.Equal(destination.Id, harness.Session.ChatId);
        Assert.Empty(harness.Session.KnowledgeFiles);
    }
}

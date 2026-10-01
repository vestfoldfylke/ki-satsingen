using System.Text.Json;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Processing;
using kisatsingen.Services.KnowledgeFiles.Reading;
using kisatsingen.Tests.Data;
using kisatsingen.Tests.Data.Repositories;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.FileTools;

// The tools against files saved through the real save path, so line numbers,
// scope and ownership are what production would see.
[Collection(PostgresCollection.Name)]
public sealed class FileToolsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";
    private const string Budget = "# Budsjett\n\nInnledning.\n\n## Drift\n\n| Post | Beløp |\n|---|---|\n| Lønn | 100 |\n";

    private KnowledgeFileRepository Files => new(fixture.Factory, maxEstimatedTokenCount: 500_000);

    private ChatRepository Chats => new(fixture.Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_files_lists_a_saved_file_with_its_id_and_line_count()
    {
        var chatId = await CreateChatAsync(OwnerId);
        var fileId = await SaveAsync(OwnerId, chatId, KnowledgeFileFactory.Draft("budsjett.md", Budget));

        var result = await InvokeAsync(OwnerId, chatId, "list_files", []);

        var file = Assert.Single(result.GetProperty("files").EnumerateArray());
        Assert.Equal(fileId.ToString(), file.GetProperty("fileId").GetString());
        Assert.Equal("budsjett.md", file.GetProperty("name").GetString());
        Assert.Equal(9, file.GetProperty("lineCount").GetInt32());
    }

    [Fact]
    public async Task List_files_notes_that_an_images_content_is_not_read()
    {
        var chatId = await CreateChatAsync(OwnerId);
        await SaveAsync(OwnerId, chatId, KnowledgeFileFactory.Draft("bilde.png", "Bilde: 10 x 10 px.") with { Origin = ContentOrigin.ImagePlaceholder });

        var result = await InvokeAsync(OwnerId, chatId, "list_files", []);

        var file = Assert.Single(result.GetProperty("files").EnumerateArray());
        Assert.Equal("Innholdet i bildet er ikke tolket.", file.GetProperty("originNote").GetString());
    }

    [Fact]
    public async Task Get_outline_returns_the_headings_and_tables_of_a_saved_file_with_their_lines()
    {
        var chatId = await CreateChatAsync(OwnerId);
        var fileId = await SaveAsync(OwnerId, chatId, KnowledgeFileFactory.Draft("budsjett.md", Budget));

        var result = await InvokeAsync(OwnerId, chatId, "get_outline", new() { ["fileId"] = fileId.ToString() });

        var entries = result.GetProperty("entries").EnumerateArray()
            .Select(entry => (entry.GetProperty("line").GetInt32(), entry.GetProperty("endLine").GetInt32(), entry.GetProperty("kind").GetString()));
        Assert.Equal([(1, 9, "heading"), (5, 9, "heading"), (7, 9, "table")], entries);
    }

    [Fact]
    public async Task Read_file_returns_the_numbered_lines_of_a_saved_file()
    {
        var chatId = await CreateChatAsync(OwnerId);
        var fileId = await SaveAsync(OwnerId, chatId, KnowledgeFileFactory.Draft("budsjett.md", Budget));

        var result = await InvokeAsync(OwnerId, chatId, "read_file", new() { ["fileId"] = fileId.ToString(), ["start"] = 1, ["end"] = 3 });

        Assert.Equal("1\t# Budsjett\n2\t\n3\tInnledning.", result.GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_file_in_another_chat_of_the_same_owner_is_not_found()
    {
        var otherChatId = await CreateChatAsync(OwnerId);
        var fileId = await SaveAsync(OwnerId, otherChatId, KnowledgeFileFactory.Draft("budsjett.md", Budget));
        var chatId = await CreateChatAsync(OwnerId);

        var result = await InvokeAsync(OwnerId, chatId, "read_file", new() { ["fileId"] = fileId.ToString(), ["start"] = 1, ["end"] = 3 });

        Assert.Equal(FileToolTexts.FileNotFound, result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Another_owners_file_is_not_found_even_through_tools_bound_to_that_owners_chat()
    {
        var theirChatId = await CreateChatAsync(OtherOwnerId);
        var fileId = await SaveAsync(OtherOwnerId, theirChatId, KnowledgeFileFactory.Draft("hemmelig.md", Budget));

        var result = await InvokeAsync(OwnerId, theirChatId, "get_outline", new() { ["fileId"] = fileId.ToString() });

        Assert.Equal(FileToolTexts.FileNotFound, result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task List_files_lists_nothing_from_another_owners_chat()
    {
        var theirChatId = await CreateChatAsync(OtherOwnerId);
        await SaveAsync(OtherOwnerId, theirChatId, KnowledgeFileFactory.Draft("hemmelig.md", Budget));

        var result = await InvokeAsync(OwnerId, theirChatId, "list_files", []);

        Assert.Empty(result.GetProperty("files").EnumerateArray());
    }

    private async Task<Guid> CreateChatAsync(string ownerId) =>
        (await Chats.CreateChatAsync(ownerId, "hello", assistantId: null)).Id;

    private async Task<Guid> SaveAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft)
    {
        var result = await Files.CreateFileForChatAsync(ownerId, chatId, draft);
        return Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File.Id;
    }

    private async Task<JsonElement> InvokeAsync(string ownerId, Guid chatId, string toolName, AIFunctionArguments arguments)
    {
        var tools = new FileToolFactory(new KnowledgeFileReader(Files), new FileToolOptions()).CreateForChat(ownerId, chatId);
        var tool = tools.Cast<AIFunction>().Single(tool => tool.Name == toolName);
        var json = Assert.IsType<string>(await tool.InvokeAsync(arguments));

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

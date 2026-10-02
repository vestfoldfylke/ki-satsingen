using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.FileTools;

// What the code promises the model: tool names, arguments and result shapes,
// pinned in full. Wording is not pinned, so it can be tuned freely; texts are
// compared through their constants, and descriptions only have to exist and
// state the configured limits.
public sealed class FileToolContractTests
{
    private const string OwnerId = "Whatever";
    private static readonly Guid ChatId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Markdown = "# Notat\n\nHei.\n";

    private static IReadOnlyList<AIFunction> Tools(ContentOrigin? origin = ContentOrigin.TextFile, FileToolOptions? options = null) =>
        [.. new FileToolFactory(new StubFileReader(OwnerId, ChatId, FileId, Markdown, origin), options ?? new FileToolOptions())
            .CreateForChat(OwnerId, ChatId)
            .Cast<AIFunction>()];

    private static IEnumerable<(string Name, string? Description)> ArgumentDescriptions(AIFunction tool) =>
        tool.JsonSchema.GetProperty("properties").EnumerateObject()
            .Select(argument => ($"{tool.Name}.{argument.Name}", argument.Value.TryGetProperty("description", out var description) ? description.GetString() : null));

    private static string DescribeArguments(AIFunction tool)
    {
        var required = tool.JsonSchema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToHashSet()
            : [];

        var arguments = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(argument =>
        {
            var schema = argument.Value;
            var type = schema.GetProperty("type") is { ValueKind: JsonValueKind.Array } types
                ? string.Join("|", types.EnumerateArray().Select(entry => entry.GetString()))
                : schema.GetProperty("type").GetString();
            var requiredMark = required.Contains(argument.Name) ? " required" : "";
            var range = schema.TryGetProperty("minimum", out var minimum)
                ? $" {minimum.GetInt32()}..{(schema.TryGetProperty("maximum", out var maximum) && maximum.GetInt32() != int.MaxValue ? maximum.GetInt32().ToString() : "")}"
                : "";
            return $"{argument.Name}: {type}{requiredMark}{range}";
        });

        return $"{tool.Name}({string.Join(", ", arguments)})";
    }

    private const string UnreadableArguments = "kunne ikke leses";

    // Through ToolInvoker, as FunctionInvokingChatClient calls the tools.
    private static async Task<string> InvokeAsync(string toolName, AIFunctionArguments arguments, ContentOrigin? origin = ContentOrigin.TextFile)
    {
        var tool = Tools(origin).Single(tool => tool.Name == toolName);
        var context = new FunctionInvocationContext { Function = tool, Arguments = arguments };
        return Assert.IsType<string>(await new ToolInvoker(NullLogger<ToolInvoker>.Instance).InvokeAsync(context, CancellationToken.None));
    }

    // In the form the provider adapter hands the tools: it parses the model's
    // JSON into a Dictionary<string, object?>, so a value is a JsonElement and
    // a JSON null is null.
    private static AIFunctionArguments Arguments(params (string Name, object? Value)[] values)
    {
        var arguments = new AIFunctionArguments();
        foreach (var (name, value) in values)
        {
            arguments[name] = value is null ? null : JsonSerializer.SerializeToElement(value);
        }

        return arguments;
    }

    [Fact]
    public void The_tools_are_list_files_get_outline_and_read_file()
    {
        Assert.Equal(["list_files", "get_outline", "read_file"], Tools().Select(tool => tool.Name));
    }

    // Wording is free to change; what the code relies on is that every tool and
    // argument is described at all.
    [Fact]
    public void Every_tool_and_every_argument_has_a_description()
    {
        var undescribed = Tools()
            .SelectMany(tool => ArgumentDescriptions(tool).Prepend((Name: tool.Name, Description: tool.Description)))
            .Where(entry => string.IsNullOrWhiteSpace(entry.Description))
            .Select(entry => entry.Name);

        Assert.Empty(undescribed);
    }

    [Fact]
    public void The_descriptions_state_the_configured_read_cap_and_outline_depth()
    {
        var options = new FileToolOptions { MaxReadTokens = 1234, DefaultOutlineDepth = 5 };

        var tools = Tools(options: options);

        Assert.Contains("1234", tools.Single(tool => tool.Name == "read_file").Description);
        Assert.Contains("5", tools.Single(tool => tool.Name == "get_outline").Description);
    }

    // Names, types, ranges and what is required: what the model must send.
    // Argument descriptions are left out, so they can be reworded freely.
    [Fact]
    public void The_tool_arguments_are_pinned()
    {
        Assert.Equal(
            [
                "list_files()",
                "get_outline(fileId: string required, start: integer|null 1.., end: integer|null 1.., depth: integer|null 1..6)",
                "read_file(fileId: string required, start: integer required 1.., end: integer required 1..)"
            ],
            Tools().Select(DescribeArguments));
    }

    [Fact]
    public async Task The_list_files_result_shape_is_pinned()
    {
        var result = await InvokeAsync("list_files", Arguments());

        Assert.Equal(
            """{"files":[{"fileId":"11111111-1111-1111-1111-111111111111","name":"notat.md","lineCount":3,"summary":"Utdrag."}]}""",
            result);
    }

    [Fact]
    public async Task The_get_outline_result_shape_is_pinned()
    {
        var result = await InvokeAsync("get_outline", Arguments(("fileId", FileId.ToString())));

        Assert.Equal(
            """{"fileId":"11111111-1111-1111-1111-111111111111","name":"notat.md","summary":"Utdrag.","lineCount":3,"entries":[{"line":1,"endLine":3,"kind":"heading","level":1,"title":"Notat"}]}""",
            result);
    }

    [Fact]
    public async Task The_read_file_result_shape_is_pinned()
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", 1), ("end", 3)));

        Assert.Equal(
            $$"""{"fileId":"11111111-1111-1111-1111-111111111111","name":"notat.md","startLine":1,"endLine":3,"totalLines":3,"contentNotice":"{{FileToolTexts.ContentNotice}}","content":"1\t# Notat\n2\t\n3\tHei."}""",
            result);
    }

    [Fact]
    public async Task The_error_result_shape_is_pinned()
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", Guid.NewGuid().ToString()), ("start", 1), ("end", 3)));

        Assert.Equal(
            $$"""{"error":"{{FileToolTexts.FileNotFound}}"}""",
            result);
    }

    [Fact]
    public async Task A_file_whose_origin_this_build_does_not_know_gets_the_most_cautious_note()
    {
        var result = await InvokeAsync("list_files", Arguments(), origin: null);

        Assert.Contains($"\"originNote\":\"{FileToolTexts.OriginNote(null)}\"", result);
    }

    [Fact]
    public void Every_content_origin_has_a_deliberate_note()
    {
        var origins = Enum.GetValues<ContentOrigin>();

        var exception = Record.Exception(() => origins.Select(origin => FileToolTexts.OriginNote(origin)).ToList());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("budsjett.md")]
    [InlineData(null)]
    public async Task A_malformed_or_null_file_id_is_answered_like_an_unknown_one(string? fileId)
    {
        var result = await InvokeAsync("get_outline", Arguments(("fileId", fileId)));

        Assert.Contains(FileToolTexts.FileNotFound, result);
    }

    [Fact]
    public async Task Line_numbers_sent_as_strings_are_accepted()
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", "3"), ("end", "3")));

        Assert.Contains("\"content\":\"3\\tHei.\"", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task A_line_number_below_1_or_null_is_refused_with_what_to_send(object? start)
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", start), ("end", 3)));

        Assert.Contains(FileToolTexts.InvalidLineNumber, result);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(1.5)]
    public async Task A_line_number_that_is_not_an_integer_is_refused_as_unreadable(object start)
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", start), ("end", 3)));

        Assert.Contains($"Argumentene til read_file {UnreadableArguments}", result);
    }

    [Fact]
    public async Task An_explicit_null_for_an_optional_argument_means_its_default()
    {
        var result = await InvokeAsync("get_outline", Arguments(("fileId", FileId.ToString()), ("start", null), ("end", null), ("depth", null)));

        Assert.Contains("\"title\":\"Notat\"", result);
    }

    [Fact]
    public async Task A_read_without_an_end_is_refused_with_the_missing_argument_named()
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", 1)));

        Assert.Contains("'end'", result);
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_refused()
    {
        var result = await InvokeAsync("read_file", Arguments(("fileId", FileId.ToString()), ("start", 3), ("end", 1)));

        Assert.Contains(FileToolTexts.EndBeforeStart, result);
    }

    [Fact]
    public async Task A_start_past_the_last_line_is_refused_with_the_files_line_count()
    {
        var result = await InvokeAsync("get_outline", Arguments(("fileId", FileId.ToString()), ("start", 10)));

        Assert.Contains(FileToolTexts.StartPastLastLine(3), result);
    }

    [Fact]
    public async Task A_capped_outline_names_every_argument_of_the_next_call_including_the_defaults()
    {
        var reader = new StubFileReader(OwnerId, ChatId, FileId, "# A\n# B\n# C");
        var tool = new FileToolFactory(reader, new FileToolOptions { MaxOutlineEntries = 1 }).CreateForChat(OwnerId, ChatId).Cast<AIFunction>().Single(tool => tool.Name == "get_outline");

        var result = await tool.InvokeAsync(Arguments(("fileId", FileId.ToString())));

        Assert.Contains(FileToolTexts.OutlineContinuation(shownCount: 1, nextLine: 2, end: 3, depth: 2), Assert.IsType<string>(result));
    }

    [Fact]
    public async Task A_cut_read_names_the_next_start_and_the_end_clamped_to_the_file()
    {
        var reader = new StubFileReader(OwnerId, ChatId, FileId, "en\nto\ntre");
        var tool = new FileToolFactory(reader, new FileToolOptions { MaxReadTokens = 1 }).CreateForChat(OwnerId, ChatId).Cast<AIFunction>().Single(tool => tool.Name == "read_file");

        var result = await tool.InvokeAsync(Arguments(("fileId", FileId.ToString()), ("start", 1), ("end", 50)));

        Assert.Contains(FileToolTexts.ReadContinuation(nextLine: 2, end: 3), Assert.IsType<string>(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task A_depth_outside_1_to_6_is_refused(int depth)
    {
        var result = await InvokeAsync("get_outline", Arguments(("fileId", FileId.ToString()), ("depth", depth)));

        Assert.Contains(FileToolTexts.InvalidDepth, result);
    }

    [Fact]
    public async Task An_outline_without_a_depth_shows_the_configured_default_depth()
    {
        var reader = new StubFileReader(OwnerId, ChatId, FileId, "# A\n## B\n");
        var tool = new FileToolFactory(reader, new FileToolOptions { DefaultOutlineDepth = 1 }).CreateForChat(OwnerId, ChatId).Cast<AIFunction>().Single(tool => tool.Name == "get_outline");

        var result = Assert.IsType<string>(await tool.InvokeAsync(Arguments(("fileId", FileId.ToString()))));

        Assert.Contains("\"title\":\"A\"", result);
        Assert.DoesNotContain("\"title\":\"B\"", result);
    }
}

using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Components.Chat;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Components.Chat;

public sealed class ToolCallViewTests
{
    public static TheoryData<string> ToolNamesTheChatOffers =>
        new([.. ChatTools.All.Select(tool => tool.Name), .. FileToolFactory.ToolNames]);

    [Fact]
    public void A_finished_read_is_titled_with_the_file_and_the_lines_read()
    {
        var view = Build("read_file", """{"fileName":"Budsjett.xlsx","startLine":1,"endLine":80,"content":""}""");

        Assert.Equal("Leste «Budsjett.xlsx», linje 1 til 80", view.Title);
    }

    [Fact]
    public void A_finished_read_shows_the_content_without_line_numbers()
    {
        var view = Build("read_file", """{"fileName":"notat.md","startLine":1,"endLine":3,"content":"1\t# Notat\n2\t\n3\tHei."}""");

        Assert.Equal("# Notat\n\nHei.", view.UserFriendlyResult);
    }

    [Fact]
    public void A_finished_read_whose_result_lacks_the_file_name_is_titled_without_it()
    {
        var view = Build("read_file", """{"startLine":1,"endLine":80}""");

        Assert.Equal("Leste filen", view.Title);
    }

    [Fact]
    public void A_refused_read_is_marked_refused_and_says_the_file_could_not_be_read()
    {
        var view = Build("read_file", """{"error":"Fant ikke filen."}""");

        Assert.Equal((ToolOutcome.Refused, "Kunne ikke lese filen"), (view.Outcome, view.Title));
    }

    [Fact]
    public void A_refused_call_has_no_user_friendly_result()
    {
        var view = Build("read_file", """{"error":"Fant ikke filen."}""");

        Assert.Null(view.UserFriendlyResult);
    }

    [Fact]
    public void A_finished_outline_lists_its_entries_with_their_lines()
    {
        var view = Build("get_outline", """
            {"fileName":"notat.md","entries":[
                {"startLine":1,"endLine":3,"kind":"heading","level":1,"title":"Notat"},
                {"startLine":2,"endLine":3,"kind":"heading","level":2,"title":"Del"}]}
            """);

        Assert.Equal("Notat (linje 1 til 3)\nDel (linje 2 til 3)", view.UserFriendlyResult);
    }

    [Fact]
    public void A_finished_file_list_lists_the_file_names()
    {
        var view = Build("list_files", """{"files":[{"fileName":"budsjett.md"},{"fileName":"notat.txt"}]}""");

        Assert.Equal("budsjett.md\nnotat.txt", view.UserFriendlyResult);
    }

    [Fact]
    public void A_file_list_without_files_says_so()
    {
        var view = Build("list_files", """{"files":[]}""");

        Assert.Equal("Ingen filer", view.UserFriendlyResult);
    }

    [Fact]
    public void The_time_is_shown_as_it_was_returned()
    {
        var view = Build("get_current_time_utc", "\"2026-11-09T09:22:33.0000000+00:00\"");

        Assert.Equal("2026-11-09T09:22:33.0000000+00:00", view.UserFriendlyResult);
    }

    [Fact]
    public void An_interrupted_call_says_it_was_interrupted_whatever_the_tool()
    {
        var view = Build("read_file", result: null, ToolStatus.Interrupted);

        Assert.Equal("Avbrutt", view.Title);
    }

    [Fact]
    public void An_unknown_tool_is_named_in_its_title()
    {
        var view = Build("search_web", "\"treff\"");

        Assert.Equal("Brukte verktøyet search_web", view.Title);
    }

    [Theory]
    [MemberData(nameof(ToolNamesTheChatOffers))]
    public void Every_tool_the_chat_offers_has_a_title_of_its_own(string toolName)
    {
        var view = Build(toolName, result: null, ToolStatus.Running);

        Assert.DoesNotContain(toolName, view.Title);
    }

    [Fact]
    public void The_tool_call_json_holds_the_tool_its_status_its_arguments_and_its_result()
    {
        var view = Build("read_file", """{"error":"Fant ikke filen."}""");

        var toolCall = JsonElement.Parse(view.ToolCallJson);
        Assert.Equal(
            ("read_file", "Completed", "f", "Fant ikke filen."),
            (toolCall.GetProperty("tool").GetString(),
                toolCall.GetProperty("status").GetString(),
                toolCall.GetProperty("arguments").GetProperty("fileId").GetString(),
                toolCall.GetProperty("result").GetProperty("error").GetString()));
    }

    [Fact]
    public void A_result_that_is_not_json_is_kept_as_text_in_the_tool_call_json()
    {
        var view = Build("read_file", "Error: Function failed.", ToolStatus.Failed);

        var toolCall = JsonElement.Parse(view.ToolCallJson);
        Assert.Equal("Error: Function failed.", toolCall.GetProperty("result").GetString());
    }

    private static ToolCallView Build(string toolName, string? result, ToolStatus status = ToolStatus.Completed) =>
        ToolCallView.Build(new ToolSegment(
            Guid.NewGuid(),
            Round: 0,
            CallId: "call-1",
            toolName,
            JsonSerializer.SerializeToElement(new { fileId = "f" }),
            status,
            result));
}

using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Components.Chat;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Components.Chat;

public sealed class ToolCallViewTests
{
    public static TheoryData<string> ToolNamesTheChatOffers =>
        new([.. ChatTools.All.Select(tool => tool.Name), .. FileToolFactory.ToolNames]);

    [Fact]
    public void A_read_of_part_of_a_file_is_titled_with_the_lines_read_and_the_line_count()
    {
        var view = Build("read_file", """{"fileName":"Budsjett.xlsx","startLine":1,"endLine":80,"lineCount":400,"content":""}""");

        Assert.Equal("Leste «Budsjett.xlsx», linje 1 til 80 av 400", view.Title);
    }

    [Fact]
    public void A_read_of_every_line_is_titled_as_the_whole_file()
    {
        var view = Build("read_file", """{"fileName":"skjermbilde.png","startLine":1,"endLine":12,"lineCount":12,"content":""}""");

        Assert.Equal("Leste hele «skjermbilde.png»", view.Title);
    }

    [Fact]
    public void A_read_of_every_line_with_a_cut_line_is_not_titled_as_the_whole_file()
    {
        var content = $"1\tKort.\n2\tLang{FileToolTexts.LineCutMarker(shownCharacters: 4, totalCharacters: 3_000)}";
        var result = JsonSerializer.Serialize(new { fileName = "notat.md", startLine = 1, endLine = 2, lineCount = 2, content });

        var view = Build("read_file", result);

        Assert.Equal("Leste «notat.md», linje 1 til 2 av 2 (lange linjer er kuttet)", view.Title);
    }

    [Fact]
    public void A_read_of_a_file_too_large_for_one_read_is_titled_as_too_large()
    {
        var view = Build("read_file", """{"fileName":"rapport.pdf","lineCount":4210,"estimatedTokens":48000,"continuation":"Bruk get_outline."}""");

        Assert.Equal(("«rapport.pdf» er for stor til å leses på én gang", null), (view.Title, view.UserFriendlyResult));
    }

    [Fact]
    public void A_finished_read_shows_the_content_without_line_numbers()
    {
        var view = Build("read_file", """{"fileName":"notat.md","startLine":1,"endLine":3,"content":"1\t# Notat\n2\t\n3\tHei."}""");

        Assert.Equal("# Notat\n\nHei.", view.UserFriendlyResult);
    }

    [Theory]
    [InlineData("""{"startLine":1,"endLine":80}""")]
    [InlineData("""{"fileName":"notat.md","startLine":1,"endLine":2}""")]
    public void A_finished_read_missing_what_its_texts_need_falls_back_to_the_plain_title(string result)
    {
        var view = Build("read_file", result);

        Assert.Equal(("Leste filen", null), (view.Title, view.UserFriendlyResult));
    }

    [Fact]
    public void A_finished_outline_whose_result_lacks_the_entries_falls_back_instead_of_failing()
    {
        var view = Build("get_outline", """{"fileName":"notat.md"}""");

        Assert.Equal(("Laget innholdsfortegnelse", null), (view.Title, view.UserFriendlyResult));
    }

    [Fact]
    public void An_outline_without_entries_says_it_is_empty()
    {
        var view = Build("get_outline", """{"fileName":"notat.md","entries":[]}""");

        Assert.Equal("Innholdsfortegnelsen er tom", view.UserFriendlyResult);
    }

    [Fact]
    public void A_refused_read_is_shown_as_a_refusal()
    {
        var view = Build("read_file", """{"error":"Fant ikke filen."}""");

        Assert.Equal(
            (ToolOutcome.Refused, "Prøvde å lese filen", (string?)null),
            (view.Outcome, view.Title, view.UserFriendlyResult));
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
    public void An_interrupted_call_says_what_was_interrupted()
    {
        var view = Build("read_file", result: null, ToolStatus.Interrupted);

        Assert.Equal("Leser filen (avbrutt)", view.Title);
    }

    [Fact]
    public void A_failed_call_says_what_failed()
    {
        var view = Build("read_file", "Error: Function failed.", ToolStatus.Failed);

        Assert.Equal("Leser filen (feilet)", view.Title);
    }

    [Fact]
    public void An_unknown_tool_is_named_in_its_title()
    {
        var view = Build("search_web", "\"treff\"");

        Assert.Equal("Brukte verktøyet search_web", view.Title);
    }

    private const string PresentationArguments = """
        { "title": "Budsjett", "theme": "VannFjord", "slides": [ { "kind": "Chapter", "title": "Innledning" } ] }
        """;

    private const string PresentationResult = """{"fileName":"Budsjett.pptx","slideCount":1}""";

    [Fact]
    public void A_created_presentation_offers_its_slides_for_download_under_its_file_name()
    {
        var view = Build("create_presentation", PresentationResult, arguments: PresentationArguments);

        Assert.Equal(
            ("Budsjett.pptx", "Budsjett", PresentationTheme.VannFjord, "Innledning"),
            (view.Download?.FileName, view.Download?.Spec.Title, view.Download?.Spec.Theme, view.Download?.Spec.Slides[0].Title));
    }

    [Fact]
    public void A_refused_presentation_offers_no_download()
    {
        var view = Build("create_presentation", """{"error":"Lysbilde 1: title er tom."}""", arguments: PresentationArguments);

        Assert.Null(view.Download);
    }

    [Fact]
    public void A_stored_presentation_whose_arguments_no_longer_make_a_presentation_offers_no_download()
    {
        var view = Build("create_presentation", PresentationResult, arguments: """{ "title": "Budsjett", "theme": "VannFjord", "slides": [ { "kind": "Picture", "title": "Bilde" } ] }""");

        Assert.Equal(("Laget presentasjon", null), (view.Title, view.Download));
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

        var toolCall = JsonElement.Parse(view.FormatToolCallJson());
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

        var toolCall = JsonElement.Parse(view.FormatToolCallJson());
        Assert.Equal("Error: Function failed.", toolCall.GetProperty("result").GetString());
    }

    private static ToolCallView Build(string toolName, string? result, ToolStatus status = ToolStatus.Completed, string arguments = """{"fileId":"f"}""") =>
        ToolCallView.Build(new ToolCall(
            Guid.NewGuid(),
            Round: 0,
            CallId: "call-1",
            toolName,
            JsonElement.Parse(arguments),
            status,
            result));
}

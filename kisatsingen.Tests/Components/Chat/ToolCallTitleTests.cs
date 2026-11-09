using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Components.Chat;
using Xunit;

namespace kisatsingen.Tests.Components.Chat;

public sealed class ToolCallTitleTests
{
    public static TheoryData<string> ToolNamesTheChatOffers =>
        new([.. ChatTools.All.Select(tool => tool.Name), .. FileToolFactory.ToolNames]);

    [Fact]
    public void A_finished_read_names_the_file_and_the_lines_read()
    {
        var result = JsonElement.Parse("""{"fileName":"Budsjett.xlsx","startLine":1,"endLine":80}""");

        var title = ToolCallTitle.Describe("read_file", ToolOutcome.Succeeded, result);

        Assert.Equal("Leste «Budsjett.xlsx», linje 1 til 80", title);
    }

    [Fact]
    public void A_finished_read_whose_result_lacks_the_file_name_is_titled_without_it()
    {
        var result = JsonElement.Parse("""{"startLine":1,"endLine":80}""");

        var title = ToolCallTitle.Describe("read_file", ToolOutcome.Succeeded, result);

        Assert.Equal("Leste filen", title);
    }

    [Fact]
    public void A_finished_outline_names_the_file()
    {
        var result = JsonElement.Parse("""{"fileName":"Budsjett.xlsx"}""");

        var title = ToolCallTitle.Describe("get_outline", ToolOutcome.Succeeded, result);

        Assert.Equal("Så på innholdsoversikten i «Budsjett.xlsx»", title);
    }

    [Fact]
    public void A_refused_read_says_the_file_could_not_be_read()
    {
        var result = JsonElement.Parse("""{"error":"Fant ikke filen."}""");

        var title = ToolCallTitle.Describe("read_file", ToolOutcome.Refused, result);

        Assert.Equal("Kunne ikke lese filen", title);
    }

    [Fact]
    public void An_interrupted_call_says_it_was_interrupted_whatever_the_tool()
    {
        var title = ToolCallTitle.Describe("read_file", ToolOutcome.Interrupted, result: null);

        Assert.Equal("Avbrutt", title);
    }

    [Fact]
    public void An_unknown_tool_is_named_in_its_title()
    {
        var title = ToolCallTitle.Describe("search_web", ToolOutcome.Succeeded, result: null);

        Assert.Equal("Brukte verktøyet search_web", title);
    }

    [Theory]
    [MemberData(nameof(ToolNamesTheChatOffers))]
    public void Every_tool_the_chat_offers_has_a_title_of_its_own(string toolName)
    {
        var title = ToolCallTitle.Describe(toolName, ToolOutcome.Running, result: null);

        Assert.DoesNotContain(toolName, title);
    }
}

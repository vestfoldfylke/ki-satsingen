using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.WordDocumentTools;
using kisatsingen.Services.MeetingDocuments;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.WordDocumentTools;

// What the tool promises the model: its name, arguments and result shape.
public sealed class CreateMeetingDocumentToolTests
{
    private static AIFunction Tool => CreateMeetingDocumentTool.Tool;

    private static async Task<JsonElement> InvokeAsync(string argumentsJson)
    {
        var arguments = new AIFunctionArguments();
        foreach (var property in JsonElement.Parse(argumentsJson).EnumerateObject())
        {
            arguments[property.Name] = property.Value;
        }

        var context = new FunctionInvocationContext { Function = Tool, Arguments = arguments };
        var result = Assert.IsType<string>(await new ToolInvoker(NullLogger<ToolInvoker>.Instance).InvokeAsync(context, CancellationToken.None));
        return JsonElement.Parse(result);
    }

    private const string ValidArguments = """
        {
          "title": "Referat styremøte",
          "kind": "Minutes",
          "meeting": "Styremøte",
          "date": "24.11.2026",
          "agendaItems": [
            { "item": "1", "description": "Budsjett", "decision": "Vedtatt" },
            { "item": "2", "description": "Eventuelt" }
          ]
        }
        """;

    [Fact]
    public void The_tool_requires_only_a_title_a_kind_a_meeting_and_agenda_items()
    {
        var required = Tool.JsonSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString());

        Assert.Equal(["title", "kind", "meeting", "agendaItems"], required);
    }

    [Fact]
    public async Task A_valid_meeting_document_is_answered_with_its_file_name_and_item_count()
    {
        var result = await InvokeAsync(ValidArguments);

        Assert.Equal(
            ("Referat styremøte.docx", 2),
            (result.GetProperty("fileName").GetString(), result.GetProperty("agendaItemCount").GetInt32()));
    }

    [Fact]
    public void The_arguments_of_an_accepted_call_read_back_as_the_meeting_document()
    {
        var spec = CreateMeetingDocumentTool.ReadArguments(JsonElement.Parse(ValidArguments));

        Assert.Equal(
            (MeetingDocumentKind.Minutes, "Styremøte", "24.11.2026", "Vedtatt"),
            (spec?.Kind, spec?.Meeting, spec?.Date, spec?.AgendaItems[0].Decision));
    }

    [Theory]
    [InlineData("""{ "title": "T", "kind": "Minutes", "meeting": "M", "agendaItems": [] }""")]
    [InlineData("""{ "title": "T", "kind": "Minutes", "meeting": "M", "agendaItems": [ null ] }""")]
    [InlineData("""{ "title": "T", "kind": "Minutes", "agendaItems": [ { "item": "1", "description": "D" } ] }""")]
    [InlineData("""[]""")]
    public void Arguments_this_build_cannot_make_a_meeting_document_of_read_back_as_nothing(string argumentsJson)
    {
        var spec = CreateMeetingDocumentTool.ReadArguments(JsonElement.Parse(argumentsJson));

        Assert.Null(spec);
    }

    [Fact]
    public async Task A_blank_optional_field_is_refused_naming_the_field()
    {
        var result = await InvokeAsync("""{ "title": "T", "kind": "Notice", "meeting": "M", "place": " ", "agendaItems": [ { "item": "1", "description": "D" } ] }""");

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains("place", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_null_agenda_item_is_refused_with_its_position_rather_than_failing_the_turn()
    {
        var result = await InvokeAsync("""{ "title": "T", "kind": "Notice", "meeting": "M", "agendaItems": [ { "item": "1", "description": "D" }, null ] }""");

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains("Sak 2", result.GetProperty("error").GetString());
    }
}

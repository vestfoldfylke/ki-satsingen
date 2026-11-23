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
    public void The_tool_requires_only_a_title_and_a_kind()
    {
        var required = Tool.JsonSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString());

        Assert.Equal(["title", "kind"], required);
    }

    [Fact]
    public void An_agenda_item_requires_only_a_description()
    {
        var itemSchema = Tool.JsonSchema.GetProperty("properties").GetProperty("agendaItems").GetProperty("items");

        var required = itemSchema.TryGetProperty("required", out var names) ? string.Join(", ", names.EnumerateArray().Select(name => name.GetString())) : "";

        Assert.Equal("description", required);
    }

    [Fact]
    public async Task A_meeting_document_without_a_meeting_name_or_item_numbers_is_made()
    {
        var result = await InvokeAsync("""{ "title": "Referat", "kind": "Minutes", "agendaItems": [ { "description": "Budsjett" } ] }""");

        Assert.Equal(1, result.GetProperty("agendaItemCount").GetInt32());
    }

    [Fact]
    public async Task A_meeting_document_without_an_agenda_is_made_with_no_items()
    {
        var result = await InvokeAsync("""{ "title": "Møteinnkalling styremøte", "kind": "Notice", "meeting": "Styremøte" }""");

        Assert.Equal(0, result.GetProperty("agendaItemCount").GetInt32());
    }

    [Theory]
    [InlineData("""{ "title": "T", "kind": "Notice", "meeting": "M" }""")]
    [InlineData("""{ "title": "T", "kind": "Notice", "meeting": "M", "agendaItems": null }""")]
    [InlineData("""{ "title": "T", "kind": "Notice", "meeting": "M", "agendaItems": [] }""")]
    public void A_stored_call_without_agenda_items_reads_back_with_an_empty_agenda(string argumentsJson)
    {
        var spec = CreateMeetingDocumentTool.ReadArguments(JsonElement.Parse(argumentsJson));

        Assert.Equal(0, spec?.AgendaItems.Count);
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
    [InlineData("""{ "title": "T", "kind": "Minutes", "meeting": "M", "agendaItems": [ null ] }""")]
    [InlineData("""{ "kind": "Minutes", "meeting": "M", "agendaItems": [ { "item": "1", "description": "D" } ] }""")]
    [InlineData("""{ "title": "T", "kind": "Minutes", "agendaItems": [ { "item": "1" } ] }""")]
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

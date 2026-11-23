using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.WordDocumentTools;
using kisatsingen.Services.WordDocuments;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.WordDocumentTools;

// What the tool promises the model: its name, arguments and result shape.
public sealed class CreateWordDocumentToolTests
{
    private static AIFunction Tool => CreateWordDocumentTool.Tool;

    // Through ToolInvoker and with JsonElement values, as the provider adapter
    // and FunctionInvokingChatClient call the tool.
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
          "title": "Skolebruksplan 2027",
          "template": "Simple",
          "blocks": [
            { "kind": "Kicker", "text": "23.11.2026" },
            { "kind": "Heading1", "text": "Skolebruksplan 2027" },
            { "kind": "Lead", "text": "Et kort sammendrag." },
            { "kind": "BulletList", "items": ["Flere elever", "Nye bygg"] },
            { "kind": "Table", "headerRow": ["Skole", "Elever"], "rows": [["Thor Heyerdahl", "1200"]] }
          ]
        }
        """;

    [Fact]
    public void The_tool_requires_a_title_a_template_and_blocks_and_leaves_the_cover_optional()
    {
        var required = Tool.JsonSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString());

        Assert.Equal(["title", "template", "blocks"], required);
    }

    [Fact]
    public void The_schema_offers_the_templates_by_name()
    {
        var schema = Tool.JsonSchema.GetRawText();

        Assert.All(Enum.GetNames<WordDocumentTemplate>(), name => Assert.Contains($"\"{name}\"", schema));
    }

    [Fact]
    public async Task A_report_with_a_cover_is_accepted()
    {
        var result = await InvokeAsync("""{ "title": "Årsrapport", "template": "Report", "subtitle": "2026", "date": "23.11.2026", "coverText": "Økonomi", "blocks": [ { "kind": "Heading2", "text": "Innledning" }, { "kind": "FactBox", "text": "Fakta" } ] }""");

        Assert.Equal("Årsrapport.docx", result.GetProperty("fileName").GetString());
    }

    [Fact]
    public void The_description_sends_meeting_documents_to_the_meeting_tool()
    {
        Assert.Contains(CreateMeetingDocumentTool.Name, Tool.Description);
    }

    [Fact]
    public void The_schema_offers_the_block_kinds_by_name()
    {
        var schema = Tool.JsonSchema.GetRawText();

        Assert.All(Enum.GetNames<DocumentBlockKind>(), name => Assert.Contains($"\"{name}\"", schema));
    }

    [Fact]
    public async Task A_valid_document_is_answered_with_its_file_name_and_block_count()
    {
        var result = await InvokeAsync(ValidArguments);

        Assert.Equal(
            ("Skolebruksplan 2027.docx", 5),
            (result.GetProperty("fileName").GetString(), result.GetProperty("blockCount").GetInt32()));
    }

    [Fact]
    public void The_arguments_of_an_accepted_call_read_back_as_the_document()
    {
        var spec = WordDocumentSpecFrom(ValidArguments);

        Assert.Equal(
            ("Skolebruksplan 2027", 5, "Nye bygg", "1200"),
            (spec?.Title, spec?.Blocks.Count, spec?.Blocks[3].Items?[1], spec?.Blocks[4].Rows?[0][1]));
    }

    [Theory]
    [InlineData("""{ "title": "T", "template": "Simple", "blocks": [ { "kind": "Picture", "text": "Bilde" } ] }""")]
    [InlineData("""{ "title": "T", "blocks": [ { "kind": "Paragraph", "text": "" } ] }""")]
    [InlineData("""{ "title": "T", "blocks": [ { "kind": "Table", "headerRow": ["A", "B"], "rows": [["a"]] } ] }""")]
    [InlineData("""{ "title": "T", "blocks": [ null ] }""")]
    [InlineData("""{ "blocks": [] }""")]
    [InlineData("""[]""")]
    public void Arguments_this_build_cannot_make_a_document_of_read_back_as_nothing(string argumentsJson)
    {
        var spec = WordDocumentSpecFrom(argumentsJson);

        Assert.Null(spec);
    }

    [Fact]
    public void A_stored_call_over_todays_size_limits_still_reads_back_as_the_document()
    {
        var longText = new string('x', WordDocumentSpecValidator.MaxTextLength + 1);

        var spec = WordDocumentSpecFrom($$"""{ "title": "T", "blocks": [ { "kind": "Heading1", "text": "A" }, { "kind": "Heading1", "text": "B" }, { "kind": "Paragraph", "text": "{{longText}}" } ] }""");

        Assert.NotNull(spec);
    }

    [Fact]
    public void A_call_stored_without_arguments_reads_back_as_nothing()
    {
        var spec = CreateWordDocumentTool.ReadArguments(default);

        Assert.Null(spec);
    }

    [Fact]
    public async Task An_invalid_document_is_refused_with_the_problem()
    {
        var result = await InvokeAsync("""{ "title": "T", "template": "Simple", "blocks": [ { "kind": "Heading2", "text": "Kapittel", "items": ["x"] } ] }""");

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains("items", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("""{ "title": "T", "template": "Simple", "blocks": null }""", "blokker")]
    [InlineData("""{ "title": "T", "template": "Simple", "blocks": [ { "kind": "Paragraph", "text": "A" }, null ] }""", "Blokk 2")]
    [InlineData("""{ "title": null, "template": "Simple", "blocks": [ { "kind": "Paragraph", "text": "A" } ] }""", "tittel")]
    public async Task A_null_argument_is_refused_with_the_problem_rather_than_failing_the_turn(string argumentsJson, string expectedProblem)
    {
        var result = await InvokeAsync(argumentsJson);

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains(expectedProblem, result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_unknown_block_kind_is_refused_rather_than_failing_the_turn()
    {
        var result = await InvokeAsync("""{ "title": "T", "template": "Simple", "blocks": [ { "kind": "Picture", "text": "Bilde" } ] }""");

        Assert.True(ToolError.IsRefusal(result));
    }

    private static WordDocumentSpec? WordDocumentSpecFrom(string argumentsJson) =>
        CreateWordDocumentTool.ReadArguments(JsonElement.Parse(argumentsJson));
}

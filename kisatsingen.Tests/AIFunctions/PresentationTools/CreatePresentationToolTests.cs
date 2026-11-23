using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.PresentationTools;

// What the tool promises the model: its name, arguments and result shape.
public sealed class CreatePresentationToolTests
{
    private static AIFunction Tool => CreatePresentationTool.Tool;

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
          "title": "Budsjett 2027",
          "theme": "GranKorn",
          "slides": [
            { "kind": "Cover", "title": "Budsjett 2027", "text": "Forslag" },
            { "kind": "Content", "title": "Hovedtall", "bullets": ["Driften øker", "Investeringer flates ut"] }
          ]
        }
        """;

    [Fact]
    public void The_tool_takes_a_title_a_theme_and_slides_all_required()
    {
        var required = Tool.JsonSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString());

        Assert.Equal(["title", "theme", "slides"], required);
    }

    [Fact]
    public void The_schema_offers_the_themes_and_slide_kinds_by_name()
    {
        var schema = Tool.JsonSchema.GetRawText();

        Assert.All(
            [.. Enum.GetNames<PresentationTheme>(), .. Enum.GetNames<SlideKind>()],
            name => Assert.Contains($"\"{name}\"", schema));
    }

    [Fact]
    public void The_schema_tells_the_model_the_colours_of_each_theme()
    {
        var themeDescription = Tool.JsonSchema.GetProperty("properties").GetProperty("theme").GetProperty("description").GetString();

        Assert.Contains("VannFjord (blue)", themeDescription);
    }

    [Fact]
    public async Task A_valid_presentation_is_answered_with_its_file_name_and_slide_count()
    {
        var result = await InvokeAsync(ValidArguments);

        Assert.Equal(
            ("Budsjett 2027.pptx", 2),
            (result.GetProperty("fileName").GetString(), result.GetProperty("slideCount").GetInt32()));
    }

    [Fact]
    public void The_arguments_of_an_accepted_call_read_back_as_the_presentation()
    {
        var spec = CreatePresentationTool.ReadArguments(JsonElement.Parse(ValidArguments));

        Assert.Equal(
            ("Budsjett 2027", PresentationTheme.GranKorn, 2, "Investeringer flates ut"),
            (spec?.Title, spec?.Theme, spec?.Slides.Count, spec?.Slides[1].Bullets?[1]));
    }

    [Theory]
    [InlineData("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Picture", "title": "Bilde" } ] }""")]
    [InlineData("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Chapter", "title": "" } ] }""")]
    [InlineData("""{ "theme": "SivStrand" }""")]
    [InlineData("""[]""")]
    public void Arguments_this_build_cannot_make_a_presentation_of_read_back_as_nothing(string argumentsJson)
    {
        var spec = CreatePresentationTool.ReadArguments(JsonElement.Parse(argumentsJson));

        Assert.Null(spec);
    }

    [Fact]
    public void A_stored_call_over_todays_size_limits_still_reads_back_as_the_presentation()
    {
        var longBullet = new string('x', PresentationSpecValidator.MaxBulletLength + 1);

        var spec = CreatePresentationTool.ReadArguments(JsonElement.Parse($$"""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Content", "title": "K", "bullets": ["{{longBullet}}"] } ] }"""));

        Assert.NotNull(spec);
    }

    [Fact]
    public void A_call_stored_without_arguments_reads_back_as_nothing()
    {
        var spec = CreatePresentationTool.ReadArguments(default);

        Assert.Null(spec);
    }

    [Fact]
    public void Arguments_with_a_null_slide_read_back_as_nothing()
    {
        var spec = CreatePresentationTool.ReadArguments(JsonElement.Parse("""{ "title": "T", "theme": "SivStrand", "slides": [ null ] }"""));

        Assert.Null(spec);
    }

    [Fact]
    public async Task An_invalid_presentation_is_refused_with_the_problem()
    {
        var result = await InvokeAsync("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Chapter", "title": "Kapittel", "bullets": ["x"] } ] }""");

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains("bullets", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("""{ "title": "T", "theme": "SivStrand", "slides": null }""", "lysbilder")]
    [InlineData("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Chapter", "title": "K" }, null ] }""", "Lysbilde 2")]
    [InlineData("""{ "title": null, "theme": "SivStrand", "slides": [ { "kind": "Chapter", "title": "K" } ] }""", "tittel")]
    public async Task A_null_argument_is_refused_with_the_problem_rather_than_failing_the_turn(string argumentsJson, string expectedProblem)
    {
        var result = await InvokeAsync(argumentsJson);

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains(expectedProblem, result.GetProperty("error").GetString());
    }

    [Fact]
    public void The_description_points_to_the_picture_list_tool_instead_of_listing_the_pictures()
    {
        var description = Tool.Description;

        Assert.Contains(ListPresentationPicturesTool.Name, description);
        Assert.All(PictureCatalog.Embedded.All, picture => Assert.DoesNotContain(picture.Id, description));
    }

    [Fact]
    public async Task A_picture_slide_with_a_catalog_picture_is_accepted()
    {
        var pictureId = PictureCatalog.Embedded.All[0].Id;

        var result = await InvokeAsync($$"""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "ChapterWithPicture", "title": "K", "pictureId": "{{pictureId}}" } ] }""");

        Assert.False(ToolError.IsRefusal(result));
    }

    [Fact]
    public async Task A_made_up_picture_id_is_refused_naming_the_id()
    {
        var result = await InvokeAsync("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "ChapterWithPicture", "title": "K", "pictureId": "solnedgang" } ] }""");

        Assert.True(ToolError.IsRefusal(result));
        Assert.Contains("solnedgang", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_unknown_slide_kind_is_refused_rather_than_failing_the_turn()
    {
        var result = await InvokeAsync("""{ "title": "T", "theme": "SivStrand", "slides": [ { "kind": "Picture", "title": "Bilde" } ] }""");

        Assert.True(ToolError.IsRefusal(result));
    }
}

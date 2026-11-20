using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.Services.Presentations.Pictures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions.PresentationTools;

// What the tool promises the model: no arguments, and every catalog picture
// by the id create_presentation takes.
public sealed class ListPresentationPicturesToolTests
{
    private static AIFunction Tool => ListPresentationPicturesTool.Tool;

    private static async Task<JsonElement> InvokeAsync()
    {
        var context = new FunctionInvocationContext { Function = Tool, Arguments = new AIFunctionArguments() };
        var result = Assert.IsType<string>(await new ToolInvoker(NullLogger<ToolInvoker>.Instance).InvokeAsync(context, CancellationToken.None));
        return JsonElement.Parse(result);
    }

    [Fact]
    public void The_tool_takes_no_arguments()
    {
        var properties = Tool.JsonSchema.TryGetProperty("properties", out var found) ? found.EnumerateObject().Count() : 0;

        Assert.Equal(0, properties);
    }

    [Fact]
    public async Task Every_catalog_picture_is_listed_with_its_id_and_description()
    {
        var result = await InvokeAsync();

        var listed = result.GetProperty("pictures").EnumerateArray()
            .Select(picture => (picture.GetProperty("pictureId").GetString(), picture.GetProperty("description").GetString()));

        Assert.Equal(PictureCatalog.Embedded.All.Select(picture => ((string?)picture.Id, (string?)picture.Description)), listed);
    }

    [Fact]
    public async Task A_catalog_with_pictures_gives_no_notice()
    {
        var result = await InvokeAsync();

        Assert.False(result.TryGetProperty("notice", out _));
    }
}

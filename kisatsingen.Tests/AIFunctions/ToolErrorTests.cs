using System.Text.Json;
using kisatsingen.AIFunctions;
using Xunit;

namespace kisatsingen.Tests.AIFunctions;

public sealed class ToolErrorTests
{
    [Fact]
    public void A_result_with_an_error_is_a_refusal()
    {
        var result = JsonElement.Parse("""{"error":"Fant ikke filen."}""");

        Assert.True(ToolError.IsRefusal(result));
    }

    [Fact]
    public void A_result_without_an_error_is_not_a_refusal()
    {
        var result = JsonElement.Parse("""{"fileName":"Budsjett.xlsx"}""");

        Assert.False(ToolError.IsRefusal(result));
    }

    [Fact]
    public void A_result_whose_error_is_null_is_not_a_refusal()
    {
        var result = JsonElement.Parse("""{"error":null,"items":[]}""");

        Assert.False(ToolError.IsRefusal(result));
    }

    [Fact]
    public void A_result_that_is_not_an_object_is_not_a_refusal()
    {
        var result = JsonElement.Parse("""["error"]""");

        Assert.False(ToolError.IsRefusal(result));
    }
}

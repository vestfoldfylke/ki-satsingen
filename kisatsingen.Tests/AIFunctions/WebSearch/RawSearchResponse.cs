using System.ClientModel.Primitives;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.

namespace kisatsingen.Tests.AIFunctions.WebSearch;

// OpenAI's raw response, built from its wire JSON, so a test sets the action
// types the billed count is read from.
internal static class RawSearchResponse
{
    // A null action is a call OpenAI sent without one.
    public static ResponseResult WithActions(params string?[] actionJsons)
    {
        var output = actionJsons.Select((action, index) =>
            $$"""{"type":"web_search_call","id":"ws_{{index}}","status":"completed"{{(action is null ? "" : $",\"action\":{action}")}}}""");
        var json = $$"""{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"search-under-test","output":[{{string.Join(",", output)}}],"parallel_tool_calls":true,"tool_choice":"auto","tools":[]}""";
        return ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }
}

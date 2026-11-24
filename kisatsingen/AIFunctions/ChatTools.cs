using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.AIFunctions.WordDocumentTools;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions;

internal static class ChatTools
{
    // Given to every selectable model: an invariant, not a per-model flag that
    // could be set wrong. The presentation and Word tools are here rather than
    // gated like the file tools: any message may be text to turn into slides or
    // a document, and a tool list that stays the same keeps the prompt cache valid.
    public static readonly IReadOnlyList<AITool> All = [CreatePresentationTool.Tool, ListPresentationPicturesTool.Tool, CreateWordDocumentTool.Tool, CreateMeetingDocumentTool.Tool];
}

using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.AIFunctions.WordDocumentTools;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions;

// Offered as one group, since whether a turn may create files is one choice.
// Not gated on the chat's content like the knowledge file tools: any message
// may be text to turn into slides or a document, and a tool list that stays
// the same through a chat keeps the prompt cache valid.
internal static class FileGenerationTools
{
    public static readonly IReadOnlyList<AITool> All = [CreatePresentationTool.Tool, ListPresentationPicturesTool.Tool, CreateWordDocumentTool.Tool, CreateMeetingDocumentTool.Tool];
}

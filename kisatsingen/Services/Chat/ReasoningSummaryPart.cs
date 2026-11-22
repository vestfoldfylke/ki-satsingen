using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace kisatsingen.Services.Chat;

// OpenAI streams a reasoning summary in parts, each opening with a bold title,
// and the adapter passes the parts on as one run of text. Which part a piece of
// text belongs to is only on the SDK's own update. The index starts at 0 for each
// reasoning item, so a part is told apart by both.
internal readonly record struct ReasoningSummaryPart(string ItemId, int Index)
{
    // Null for reasoning that is not part of an OpenAI summary.
#pragma warning disable OPENAI001 // The Responses API is experimental in the OpenAI SDK.
    public static ReasoningSummaryPart? Of(ChatResponseUpdate update) =>
        update.RawRepresentation is StreamingResponseReasoningSummaryTextDeltaUpdate delta
            ? new ReasoningSummaryPart(delta.ItemId, delta.SummaryIndex)
            : null;
#pragma warning restore OPENAI001
}

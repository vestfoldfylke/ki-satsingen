using kisatsingen.Data.Entities;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Services.Chat;

// A chat is one ordered sequence of things that happened. The UI and the model
// are both projections of it, with different filters: the UI shows everything,
// the model sees messages only. Keeping events out of the model is a property of
// this type hierarchy rather than of a condition someone has to remember —
// an EventEntry simply is not a ChatMessage.
internal abstract record TranscriptEntry(Guid ViewId);

internal sealed record MessageEntry(
    Guid ViewId,
    ChatMessage Message,
    // Passed at construction so a message cannot exist without the metadata that
    // describes it, which two separate statements could not guarantee.
    TurnMetadata? Metadata) : TranscriptEntry(ViewId)
{
    public bool IsUser => Message.Role == ChatRole.User;
    public bool IsAssistant => Message.Role == ChatRole.Assistant;
}

internal sealed record EventEntry(
    Guid ViewId,
    ChatEventKind Kind,
    string? Detail,
    DateTimeOffset At) : TranscriptEntry(ViewId);

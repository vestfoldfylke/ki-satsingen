namespace kisatsingen.Data.Entities;

// Kept in its own table rather than sharing ChatMessages: the two have no columns
// in common beyond identity and ordering, and separate tables are what make it
// impossible for an event to be mistaken for a message on the way to the model.
public sealed class ChatEvent
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }

    // Orders this event against messages in the same chat. Drawn from the same
    // sequence they are, so the two tables interleave into one transcript.
    public long Seq { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public required ChatEventKind Kind { get; init; }

    // User-facing text only. Never an exception message — that leaks connection
    // strings and provider error bodies into something we persist and render.
    public string? Detail { get; init; }

    public Chat? Chat { get; init; }
}

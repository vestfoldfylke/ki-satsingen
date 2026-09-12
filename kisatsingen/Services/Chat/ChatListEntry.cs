namespace kisatsingen.Services.Chat;

// One row in the sidebar. A chat only earns a row after its first user turn
// lands in the database — no drafts, no ghost rows.
public sealed record ChatListEntry(Guid Id, string Title, DateTimeOffset UpdatedAt);

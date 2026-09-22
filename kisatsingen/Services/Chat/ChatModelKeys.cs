namespace kisatsingen.Services.Chat;

// The persisted vocabulary. These strings end up in ChatMessage.ModelKey, so
// they are database values in everything but name: a model can be renamed,
// repointed at a newer provider model or dropped entirely, but its key may never
// be edited or reused for something else without orphaning the rows that carry
// it.
public static class ChatModelKeys
{
    public static readonly ChatModelKey Fast = new("fast");
}

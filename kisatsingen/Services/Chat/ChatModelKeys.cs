namespace kisatsingen.Services.Chat;

// Database values in everything but name: a model can be renamed, repointed or
// dropped, but its key must never be edited or reused.
public static class ChatModelKeys
{
    public static readonly ChatModelKey Fast = new("fast");

    public static readonly ChatModelKey Large = new("large");
}

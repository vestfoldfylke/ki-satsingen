namespace kisatsingen.Services.Chat;

// Database values in everything but name: a model can be renamed, repointed or
// dropped, but its key must never be edited or reused.
public static class ChatModelKeys
{
    public static readonly ChatModelKey OpenAI = new("openai");
    public static readonly ChatModelKey Mistral = new("mistral");

    public static readonly ChatModelKey Testing = new("testing");
}

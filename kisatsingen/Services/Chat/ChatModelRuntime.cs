using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// Kept apart from ChatModel so the UI never holds a live client.
public sealed class ChatModelRuntime
{
    private readonly ChatOptions _optionsTemplate;

    internal ChatModelRuntime(ChatModel model, IChatClient client, ChatOptions optionsTemplate)
    {
        Model = model;
        Client = client;
        _optionsTemplate = optionsTemplate;
    }

    public ChatModel Model { get; }

    public IChatClient Client { get; }

    // A clone every call: callers write the turn's Instructions onto it, and a
    // shared template would let concurrent turns overwrite each other's system
    // prompt — silently, since the wrong prompt still produces an answer.
    public ChatOptions CreateOptions() => _optionsTemplate.Clone();
}

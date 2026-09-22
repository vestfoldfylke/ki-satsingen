using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

// The runnable half of a catalogued model — what it takes to actually send a
// turn. Kept apart from ChatModel, which is the half the UI renders.
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

    // A fresh instance every call, never the template itself. ChatOptions is
    // mutable — which is why it ships Clone() — and every caller writes the
    // turn's instructions onto what it gets back. Handing out the template would
    // have concurrent turns overwriting each other's system prompt.
    //
    // The clone is the API rather than a rule callers must remember, because the
    // failure it prevents is invisible: the wrong prompt still produces an answer.
    public ChatOptions CreateOptions() => _optionsTemplate.Clone();
}

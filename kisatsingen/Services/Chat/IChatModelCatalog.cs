using System.Diagnostics.CodeAnalysis;

namespace kisatsingen.Services.Chat;

// Immutable after startup; which model a chat is using is ChatSession's state.
public interface IChatModelCatalog
{
    // What a new chat starts on and a chat falls back to. Per-user gating must
    // redesign this together with Models: a default the user may not use is worse
    // than none.
    ChatModel Default { get; }

    // The authoritative allow-list, not a display list; see ChatSession.SelectModelAsync.
    IReadOnlyList<ChatModel> Models { get; }

    bool TryGet(ChatModelKey key, [MaybeNullWhen(false)] out ChatModel model);

    // Throws rather than falling back: a silent substitute would answer with a
    // model nobody chose, and the transcript would record it as chosen.
    ChatModelRuntime Resolve(ChatModelKey key);
}

using System.Diagnostics.CodeAnalysis;

namespace kisatsingen.Services.Chat;

// The set of models a user may chat with. Immutable after startup — it is a
// lookup, not a manager; the mutable part, which model a chat is currently
// using, belongs to ChatSession.
public interface IChatModelCatalog
{
    // The catalogue-wide fallback: what a new chat starts on, and what a chat
    // falls back to when the key it was last using no longer exists.
    //
    // When per-user filtering lands, Default and the allow-list have to be
    // designed together — a default the caller is not allowed to use is worse
    // than no default. That means changing this shape then, not adding a
    // user-shaped hook now that half-implements it.
    ChatModel Default { get; }

    // Every catalogued model, in registration order. Callers must treat the
    // result as the authoritative allow-list rather than a display convenience
    // — see ChatSession.SelectModelAsync. When per-user gating lands, this
    // becomes a user-parameterised accessor (and Default is redesigned with it).
    IReadOnlyList<ChatModel> Models { get; }

    bool TryGet(ChatModelKey key, [MaybeNullWhen(false)] out ChatModel model);

    // For a key that has already been checked against Models. Throws on an
    // unknown key rather than falling back to the default: a silent substitution
    // here would answer with a model the caller did not ask for, and the
    // transcript would record it as if it had been chosen.
    ChatModelRuntime Resolve(ChatModelKey key);
}

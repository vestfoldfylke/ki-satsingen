using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace kisatsingen.Services.Chat;

// The set of models a user may chat with. Immutable after startup — it is a
// lookup, not a manager; the mutable part, which model a chat is currently
// using, belongs to ChatSession.
public interface IChatModelCatalog
{
    // The catalogue-wide fallback: what a new chat starts on, and what a chat
    // falls back to when the key it was last using no longer exists.
    //
    // When per-user filtering lands, this has to be intersected with ModelsFor —
    // a default the caller is not allowed to use is worse than no default.
    ChatModel Default { get; }

    // Takes the user although it currently ignores them. This is the one part of
    // the catalogue that is expensive to retrofit: adding the parameter later
    // means finding every call site, whereas leaving it unused costs nothing.
    // Callers must treat the result as the authoritative allow-list rather than a
    // display convenience — see ChatSession.SelectModelAsync.
    IReadOnlyList<ChatModel> ModelsFor(ClaimsPrincipal user);

    bool TryGet(ChatModelKey key, [MaybeNullWhen(false)] out ChatModel model);

    // For a key that has already been checked against ModelsFor. Throws on an
    // unknown key rather than falling back to the default: a silent substitution
    // here would answer with a model the caller did not ask for, and the
    // transcript would record it as if it had been chosen.
    ChatModelRuntime Resolve(ChatModelKey key);
}

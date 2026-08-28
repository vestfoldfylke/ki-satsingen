using Microsoft.Extensions.AI;

namespace kisatsingen.Components.Chat;

public abstract record ChatMessageView(Guid Id, ChatRole Role);

public sealed record CommittedMessage(
    Guid Id,
    ChatRole Role,
    string Text,
    IList<AIContent> Contents) : ChatMessageView(Id, Role);

public sealed record StreamingMessage(
    Guid Id,
    string Text) : ChatMessageView(Id, ChatRole.Assistant);
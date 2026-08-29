using Microsoft.Extensions.AI;

namespace kisatsingen.Services.Chat;

public sealed record ChatMessageView(
    Guid Id,
    ChatRole Role,
    string Text,
    IList<AIContent> Contents);

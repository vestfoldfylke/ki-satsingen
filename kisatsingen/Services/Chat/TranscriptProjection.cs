namespace kisatsingen.Services.Chat;

// The reader's view of the transcript: user bubbles, assistant turns, and the
// events that explain a turn that produced nothing. Pure, so turn grouping can
// be tested without standing up a ChatSession and its dependencies.
internal static class TranscriptProjection
{
    public static IReadOnlyList<ChatItemView> Build(IReadOnlyList<TranscriptEntry> entries)
    {
        var views = new List<ChatItemView>();
        List<MessageEntry>? openTurn = null;

        foreach (var entry in entries)
        {
            switch (entry)
            {
                case MessageEntry { IsUser: true } user:
                    Flush();
                    views.Add(new UserBubbleView(user.ViewId, user.Message.Text ?? string.Empty));
                    break;

                case MessageEntry assistantOrTool:
                    (openTurn ??= []).Add(assistantOrTool);
                    break;

                // An event closes the turn it belongs to, so it renders after
                // whatever that turn managed to produce — which for a stop before
                // the first token is nothing at all.
                case EventEntry chatEvent:
                    Flush();
                    views.Add(new ChatEventView(chatEvent.ViewId, chatEvent.Kind, chatEvent.Detail, chatEvent.At));
                    break;
            }
        }

        Flush();
        return views;

        void Flush()
        {
            if (openTurn is null)
            {
                return;
            }

            views.Add(BuildTurnView(openTurn));
            openTurn = null;
        }
    }

    private static AssistantTurnView BuildTurnView(List<MessageEntry> turn)
    {
        var parts = new List<TurnPart>(turn.Count);
        foreach (var entry in turn)
        {
            parts.Add(new TurnPart(entry.Message.Text ?? string.Empty, entry.Message.Contents));
        }

        TurnMetadata? firstMetadata = null;
        TurnMetadata? lastMetadata = null;
        foreach (var entry in turn)
        {
            if (!entry.IsAssistant || entry.Metadata is not { } metadata)
            {
                continue;
            }

            firstMetadata ??= metadata;
            lastMetadata = metadata;
        }

        return new AssistantTurnView(turn[0].ViewId, parts, lastMetadata, firstMetadata?.CreatedAt);
    }
}

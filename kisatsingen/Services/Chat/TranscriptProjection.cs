namespace kisatsingen.Services.Chat;

// Pure, so turn grouping is testable without a ChatSession.
internal static class TranscriptProjection
{
    public static IReadOnlyList<ChatItemView> Build(IReadOnlyList<TranscriptEntry> entries)
    {
        var views = new List<ChatItemView>();
        List<MessageEntry>? openTurn = null;

        // For UserBubbleView.ModelChangedTo.
        ChatModelKey? previousTurnModel = null;
        var openingQuestionIndex = -1;

        foreach (var entry in entries)
        {
            switch (entry)
            {
                case MessageEntry { IsUser: true } user:
                    Flush();
                    openingQuestionIndex = views.Count;
                    views.Add(new UserBubbleView(user.ViewId, user.Message.Text ?? string.Empty));
                    break;

                case MessageEntry assistantOrTool:
                    (openTurn ??= []).Add(assistantOrTool);
                    break;

                // Closes its turn, so it renders after whatever the turn produced.
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

            var turnView = BuildTurnView(openTurn);
            views.Add(turnView);

            // The answering model is only known once the turn closes, so its question
            // is annotated in place. A null on either side means rows older than the
            // picker, not a change of model.
            if (turnView.Metadata?.ModelKey is { } answeringModel)
            {
                var changed = previousTurnModel is { } earlier && earlier != answeringModel;
                previousTurnModel = answeringModel;

                if (changed && openingQuestionIndex >= 0 && views[openingQuestionIndex] is UserBubbleView question)
                {
                    views[openingQuestionIndex] = question with
                    {
                        ModelChangedTo = turnView.Metadata.ModelDisplayName ?? answeringModel.Value
                    };
                }
            }

            openTurn = null;
            openingQuestionIndex = -1;
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

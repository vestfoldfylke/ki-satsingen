using kisatsingen.AIFunctions.WebSearch;

namespace kisatsingen.Services.Chat;

// Which tools one turn is offered, decided when it is sent.
internal sealed record TurnToolDecision(
    bool ShouldOfferFileGenerationTools,
    bool ShouldOfferKnowledgeFileTools,
    WebSearchAccess WebSearchAccess);

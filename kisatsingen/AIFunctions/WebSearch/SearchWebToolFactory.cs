using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.WebSearch;

// A turn's web search: the search_web tool for that turn, and the quota that
// limits it and records what it spends.
internal sealed record TurnWebSearch(AITool Tool, WebSearchQuota Quota);

// Gives each turn its own search tool, so each turn's spend is kept apart.
public sealed class SearchWebToolFactory
{
    public static readonly IReadOnlySet<string> ToolNames = new HashSet<string> { SearchWebTool.Name };

    private readonly WebSearchModel? _model;
    private readonly ILogger<SearchWebToolFactory> _logger;

    internal SearchWebToolFactory(WebSearchModel? model, ILogger<SearchWebToolFactory> logger)
    {
        _model = model;
        _logger = logger;
    }

    internal bool IsAvailable => _model is not null;

    // Null without a search model: the turn goes on without search.
    internal TurnWebSearch? StartTurn(bool isWebSearchAllowed)
    {
        if (_model is null)
        {
            return null;
        }

        var quota = new WebSearchQuota(provider: _model.Provider, modelId: _model.ModelId);
        return new TurnWebSearch(SearchWebTool.Create(_model, quota, isWebSearchAllowed: isWebSearchAllowed, logger: _logger), quota);
    }
}

using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.WebSearch;

// The tool and what it spends, for one turn: the turn offers the tool and
// records the spend when it ends.
internal sealed record SearchWebTurn(AITool Tool, WebSearchQuota Quota);

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

    // Null without a search model: the turn goes on without search.
    internal SearchWebTurn? StartTurn()
    {
        if (_model is null)
        {
            return null;
        }

        var quota = new WebSearchQuota(_model.Provider, _model.ModelId);
        return new SearchWebTurn(SearchWebTool.Create(_model, quota, _logger), quota);
    }
}

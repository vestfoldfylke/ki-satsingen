using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.WebSearch;

// How many searches one turn may make, and what its search_web calls spent,
// written as one usage row when the turn ends. Locked: the chat client calls
// tools one at a time by default, but this stays right if that changes.
internal sealed class WebSearchQuota(string provider, string modelId)
{
    public const int MaxSearchesPerTurn = 3;

    private readonly Lock _lock = new();
    private readonly UsageDetails _usage = new();
    private int _startedSearches;
    private int _billedSearches;

    public string Provider { get; } = provider;

    public string ModelId { get; } = modelId;

    public int BilledSearches
    {
        get
        {
            lock (_lock)
            {
                return _billedSearches;
            }
        }
    }

    public MessageUsage? Usage
    {
        get
        {
            lock (_lock)
            {
                return MessageUsage.FromCounts(
                    inputTokens: _usage.InputTokenCount,
                    outputTokens: _usage.OutputTokenCount,
                    estimatedInputTokens: null,
                    estimatedOutputTokens: null,
                    cachedInputTokens: _usage.CachedInputTokenCount,
                    reasoningTokens: _usage.ReasoningTokenCount);
            }
        }
    }

    // Checked before the search, so the call over the limit costs nothing. A
    // search that then fails still counts, so a failing provider is not retried
    // without end.
    public bool TryStartSearch()
    {
        lock (_lock)
        {
            if (_startedSearches >= MaxSearchesPerTurn)
            {
                return false;
            }

            _startedSearches++;
            return true;
        }
    }

    public void Add(UsageDetails? usage, int billedSearches)
    {
        lock (_lock)
        {
            if (usage is not null)
            {
                _usage.Add(usage);
            }

            _billedSearches += billedSearches;
        }
    }
}

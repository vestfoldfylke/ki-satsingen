using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions.WebSearch;

// How many search_web calls one turn may make, and what those calls spent,
// written as one usage row when the turn ends. Locked, so it stays right if
// tools are called concurrently.
internal sealed class WebSearchQuota(string provider, string modelId)
{
    public const int MaxSearchWebCallsPerTurn = 3;

    private readonly Lock _lock = new();
    private readonly UsageDetails _usage = new();
    private int _startedSearchWebCalls;
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
    public bool TryStartSearchWebCall()
    {
        lock (_lock)
        {
            if (_startedSearchWebCalls >= MaxSearchWebCallsPerTurn)
            {
                return false;
            }

            _startedSearchWebCalls++;
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

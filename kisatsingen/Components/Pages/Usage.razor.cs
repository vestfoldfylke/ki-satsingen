using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

[Authorize(Policy = "IsAdministrator")]
public partial class Usage : ComponentBase
{
    [Inject]
    public required IChatModelCatalog ChatModelCatalog { get; init; }

    [Inject]
    public required ITokenUsageRepository TokenUsageRepository { get; init; }

    private const int TopUserCount = 5;

    private IReadOnlyList<TokenUsage> OrganizationTokenUsage { get; set; } = [];
    /// <summary>
    ///  Used for TopUserCount queries
    /// </summary>
    private IReadOnlyList<TokenUsage> TokenUsagePerUser { get; set; } = [];

    private IReadOnlyList<ChatModel> ChatModels => ChatModelCatalog.Models;

    private long TotalInputTokenUsage => OrganizationTokenUsage.Sum(tokenUsage => tokenUsage.InputTokens ?? 0);
    private long TotalOutputTokenUsage => OrganizationTokenUsage.Sum(tokenUsage => tokenUsage.OutputTokens ?? 0);
    private long TotalEstimatedInputTokenUsage => OrganizationTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedInputTokens ?? 0);
    private long TotalEstimatedOutputTokenUsage => OrganizationTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedOutputTokens ?? 0);

    private IEnumerable<TokenUsage> TopTokenUsageByInputTokens => TokenUsagePerUser.OrderByDescending(tokenUsage => tokenUsage.InputTokens).Take(TopUserCount);
    private IEnumerable<TokenUsage> TopTokenUsageByOutputTokens => TokenUsagePerUser.OrderByDescending(tokenUsage => tokenUsage.OutputTokens).Take(TopUserCount);
    private IEnumerable<TokenUsage> TopTokenUsageByEstimatedInputTokens => TokenUsagePerUser.OrderByDescending(tokenUsage => tokenUsage.EstimatedInputTokens).Take(TopUserCount);
    private IEnumerable<TokenUsage> TopTokenUsageByEstimatedOutputTokens => TokenUsagePerUser.OrderByDescending(tokenUsage => tokenUsage.EstimatedOutputTokens).Take(TopUserCount);

    private TokenUsageSummary TokenUsageSummaryByModelId(string modelId)
    {
        var modelUsage = OrganizationTokenUsage
            .Where(tokenUsage => tokenUsage.ModelId == modelId)
            .ToList();

        return new TokenUsageSummary(
            modelUsage.Sum(tu => tu.InputTokens ?? 0),
            modelUsage.Sum(tu => tu.OutputTokens ?? 0),
            modelUsage.Sum(tu => tu.EstimatedInputTokens ?? 0),
            modelUsage.Sum(tu => tu.EstimatedOutputTokens ?? 0),
            modelUsage.Count);
    }

    protected override async Task OnInitializedAsync()
    {
        OrganizationTokenUsage = await TokenUsageRepository.GetOrganizationUsageAsync();

        var ownerIds = OrganizationTokenUsage.Select(tokenUsage => tokenUsage.OwnerId).Distinct().ToArray();

        IList<TokenUsage> userTokenUsage = [];

        foreach (var ownerId in ownerIds)
        {
            var ownerTokenUsage = OrganizationTokenUsage.Where(tokenUsage => tokenUsage.OwnerId == ownerId).ToList();
            userTokenUsage.Add(new TokenUsage
            {
                OwnerId = ownerId,
                ModelId = "",
                Provider = "",
                Status = TurnStatus.Completed,
                InputTokens = ownerTokenUsage.Sum(tokenUsage => tokenUsage.InputTokens),
                OutputTokens = ownerTokenUsage.Sum(tokenUsage => tokenUsage.OutputTokens),
                EstimatedInputTokens = ownerTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedInputTokens),
                EstimatedOutputTokens = ownerTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedOutputTokens),
                Id = Guid.Empty,
                Timestamp =  DateTime.UtcNow
            });
        }

        TokenUsagePerUser = [.. userTokenUsage];
    }
}

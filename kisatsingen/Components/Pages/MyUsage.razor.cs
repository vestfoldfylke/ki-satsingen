using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public partial class MyUsage : ComponentBase
{
    [Inject]
    public required IAuthenticationService AuthenticationService { private get; set; }
    
    [Inject]
    public required ITokenUsageRepository TokenUsageRepository { get; set; }

    private IReadOnlyList<TokenUsage> MyTokenUsage { get; set; } = [];

    private long TotalInputTokenUsage => MyTokenUsage.Sum(tokenUsage => tokenUsage.InputTokens ?? 0);
    private long TotalOutputTokenUsage => MyTokenUsage.Sum(tokenUsage => tokenUsage.OutputTokens ?? 0);
    private long TotalEstimatedInputTokenUsage => MyTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedInputTokens ?? 0);
    private long TotalEstimatedOutputTokenUsage => MyTokenUsage.Sum(tokenUsage => tokenUsage.EstimatedOutputTokens ?? 0);

    protected override async Task OnInitializedAsync()
    {
        var ownerId = await AuthenticationService.GetUserObjectIdentifierAsync();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new UnauthorizedAccessException();
        }

        MyTokenUsage = await TokenUsageRepository.GetMyTokenUsageAsync(ownerId);
    }
}

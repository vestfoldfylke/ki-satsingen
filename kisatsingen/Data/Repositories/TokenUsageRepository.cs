using kisatsingen.Data.Entities;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class TokenUsageRepository(IDbContextFactory<AppDbContext> factory) : ITokenUsageRepository
{
    public async Task InsertTokenUsageAsync(
        string ownerId,
        string provider,
        string modelId,
        long? inputTokens,
        long? outputTokens,
        long? estimatedInputTokens,
        long? estimatedOutputTokens,
        TurnStatus status,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Timestamp = DateTimeOffset.UtcNow,
            Provider = provider,
            ModelId = modelId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedInputTokens = estimatedInputTokens,
            EstimatedOutputTokens = estimatedOutputTokens,
            Status = status
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TokenUsage>> GetMyTokenUsageAsync(string ownerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return [.. db.TokenUsages.Where(tokenUsage => tokenUsage.OwnerId == ownerId).AsNoTracking()];
    }

    [Authorize(Policy = "IsAdministrator")]
    public async Task<IReadOnlyList<TokenUsage>> GetOrganizationUsageAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return [.. db.TokenUsages.AsNoTracking()];
    }
}

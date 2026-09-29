using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Entities;

public sealed class Consumption
{
    public const int MaxOwnerIdLength = 128;
    public const int MaxProviderLength = 64;
    public const int MaxModelIdLength = 128;
    public const int MaxStatusLength = 32;

    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
    public long TokenCount { get; init; }
    public required TurnStatus Status { get; init; }
}

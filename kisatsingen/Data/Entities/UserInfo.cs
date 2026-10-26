namespace kisatsingen.Data.Entities;

public sealed class UserInfo
{
    public required Guid Id { get; init; }
    public string? Company { get; init; }
    public string? Department { get; init; }
    public string? Name { get; init; }
    public string? Username { get; init; }
}
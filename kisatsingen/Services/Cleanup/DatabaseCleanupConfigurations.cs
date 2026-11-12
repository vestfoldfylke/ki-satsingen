namespace kisatsingen.Services.Cleanup;

public sealed class DatabaseCleanupConfigurations
{
    public const string SectionName = "DatabaseCleanup";

    public DatabaseChatCleanupConfiguration ChatCleanup { get; init; } = new();

    public void Validate() => ChatCleanup.Validate();
}

public sealed class DatabaseChatCleanupConfiguration : IDatabaseCleanupConfiguration
{
    public TimeSpan? Retention { get; init; }

    public TimeSpan? TargetTime { get; init; }

    public bool Enabled => Retention is not null && TargetTime is not null;

    private const string ConfigPath = $"{DatabaseCleanupConfigurations.SectionName}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)}";

    public void Validate()
    {
        if (Retention is { } retention && retention < TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(Retention), $"{ConfigPath}.{nameof(Retention)} must be at least 1 day. Got {retention}");
        }

        if (TargetTime is { } targetTime && (targetTime < TimeSpan.Zero || targetTime >= TimeSpan.FromDays(1)))
        {
            throw new ArgumentOutOfRangeException(nameof(TargetTime), $"{ConfigPath}.{nameof(TargetTime)} must be a time of day between 00:00:00 and 23:59:59. Got {targetTime}");
        }
    }
}

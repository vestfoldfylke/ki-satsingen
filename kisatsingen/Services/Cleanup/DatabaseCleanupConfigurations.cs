using System.Globalization;

namespace kisatsingen.Services.Cleanup;

public sealed class DatabaseCleanupConfigurations
{
    public const string SectionName = "DatabaseCleanup";

    public DatabaseChatCleanupConfiguration ChatCleanup { get; init; } = new();

    public void Validate() => ChatCleanup.Validate();
}

public sealed class DatabaseChatCleanupConfiguration
{
    public string Retention { get; init; } = "";
    public TimeSpan RetentionTimeSpan { get; private set; } = TimeSpan.Zero;

    public string TargetTime { get; init; } = "";
    public TimeSpan TargetTimeTimeSpan { get; private set; } = TimeSpan.Zero;

    private const string ConfigPath = $"{DatabaseCleanupConfigurations.SectionName}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)}";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Retention))
        {
            throw new InvalidOperationException($"{nameof(Retention)} setting is missing in section {ConfigPath} in configuration.");
        }

        if (string.IsNullOrWhiteSpace(TargetTime))
        {
            throw new InvalidOperationException($"{nameof(TargetTime)} setting is missing in section {ConfigPath} in configuration.");
        }

        if (!TimeSpan.TryParse(Retention, CultureInfo.InvariantCulture, out var retentionTimeSpan))
        {
            throw new FormatException($"Invalid format on setting {ConfigPath}.{nameof(Retention)} in configuration. Expected format: 'd.HH:mm:ss'.");
        }

        if (retentionTimeSpan <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Retention), $"{ConfigPath}.{nameof(Retention)} must be greater than zero.");
        }

        RetentionTimeSpan = retentionTimeSpan;

        if (!TimeSpan.TryParse(TargetTime, CultureInfo.InvariantCulture, out var targetTimeTimeSpan))
        {
            throw new FormatException($"Invalid format on setting {ConfigPath}.{nameof(TargetTime)} in configuration. Expected format: 'HH:mm:ss'.");
        }

        if (targetTimeTimeSpan < TimeSpan.Zero || targetTimeTimeSpan >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(TargetTime), $"{ConfigPath}.{nameof(TargetTime)} must be a time of day between 00:00:00 and 23:59:59.");
        }

        TargetTimeTimeSpan = targetTimeTimeSpan;
    }
}

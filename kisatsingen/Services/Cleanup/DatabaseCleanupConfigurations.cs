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

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Retention))
        {
            throw new InvalidOperationException($"{nameof(Retention)} setting is missing in section {nameof(DatabaseCleanupConfigurations)}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)} in configuration.");
        }

        if (string.IsNullOrWhiteSpace(TargetTime))
        {
            throw new InvalidOperationException($"{nameof(TargetTime)} setting is missing in section {nameof(DatabaseCleanupConfigurations)}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)} in configuration.");
        }

        if (!TimeSpan.TryParse(Retention, CultureInfo.InvariantCulture, out var retentionTimeSpan))
        {
            throw new FormatException($"Invalid format on setting {DatabaseCleanupConfigurations.SectionName}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)}.{nameof(DatabaseCleanupConfigurations.ChatCleanup.Retention)} in configuration.");
        }

        RetentionTimeSpan = retentionTimeSpan;

        if (!TimeSpan.TryParse(TargetTime, CultureInfo.InvariantCulture, out var targetTimeTimeSpan))
        {
            throw new FormatException($"Invalid format on setting {DatabaseCleanupConfigurations.SectionName}.{nameof(DatabaseCleanupConfigurations.ChatCleanup)}.{nameof(DatabaseCleanupConfigurations.ChatCleanup.TargetTime)}.");
        }

        TargetTimeTimeSpan = targetTimeTimeSpan;
    }
}

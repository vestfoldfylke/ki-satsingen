namespace kisatsingen.Services.Cleanup;

public interface IDatabaseCleanupConfiguration
{
    TimeSpan? Retention { get; init; }

    TimeSpan? TargetTime { get; init; }

    bool Enabled { get; }

    void Validate();
}

using System.Reflection;
using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Cleanup;

public sealed class DatabaseCleanupBackgroundService : BackgroundService
{
    private readonly IDatabaseCleanupRepository _databaseCleanupRepository;
    private readonly ILogger<DatabaseCleanupBackgroundService> _logger;

    private readonly TimeSpan _chatCleanupRetention;
    private readonly TimeSpan _chatCleanupTargetTime;

    public DatabaseCleanupBackgroundService(DatabaseCleanupConfigurations databaseCleanupConfigurations, IDatabaseCleanupRepository databaseCleanupRepository, ILogger<DatabaseCleanupBackgroundService> logger)
    {
        _databaseCleanupRepository = databaseCleanupRepository;
        _logger = logger;

        _chatCleanupRetention = databaseCleanupConfigurations.ChatCleanup.RetentionTimeSpan;
        _chatCleanupTargetTime = databaseCleanupConfigurations.ChatCleanup.TargetTimeTimeSpan;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {ClassName} jobs", nameof(DatabaseCleanupBackgroundService));

        var cleanUpJobTask = RunDelayedJobAsync(_chatCleanupTargetTime, RunChatCleanupJobAsync, stoppingToken);

        await Task.WhenAll(cleanUpJobTask);
    }

    private static TimeSpan CalculateDelayToNextOccurence(TimeSpan targetTime)
    {
        var now = DateTime.Now;
        var nextRun = now.Date + targetTime;

        if (now >= nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private async Task RunDelayedJobAsync(TimeSpan timeTarget, Func<CancellationToken, Task> jobMethod, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CalculateDelayToNextOccurence(timeTarget);

            try
            {
                _logger.LogInformation("Starting {JobName} task delay of {Delay}", jobMethod.GetMethodInfo().Name, delay);
                await Task.Delay(delay, stoppingToken);

                _logger.LogInformation("Calling {JobName} task", jobMethod.GetMethodInfo().Name);
                await jobMethod(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // To avoid possible multiple triggering in the same second
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task RunChatCleanupJobAsync(CancellationToken stoppingToken)
    {
        try
        {
            stoppingToken.ThrowIfCancellationRequested();
            _logger.LogInformation("{MethodName} job called", nameof(RunChatCleanupJobAsync));

            await _databaseCleanupRepository.CleanupChats(_chatCleanupRetention, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occured while executing job {MethodName}", nameof(RunChatCleanupJobAsync));
        }
    }
}

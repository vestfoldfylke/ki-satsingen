using System.Reflection;
using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Cleanup;

public sealed class DatabaseCleanupBackgroundService : BackgroundService
{
    private readonly IDatabaseCleanupRepository _databaseCleanupRepository;
    private readonly ILogger<DatabaseCleanupBackgroundService> _logger;

    private readonly bool _chatCleanupEnabled;
    private readonly TimeSpan _chatCleanupRetention;
    private readonly TimeSpan _chatCleanupTargetTime;

    public DatabaseCleanupBackgroundService(DatabaseCleanupConfigurations databaseCleanupConfigurations, IDatabaseCleanupRepository databaseCleanupRepository, ILogger<DatabaseCleanupBackgroundService> logger)
    {
        _databaseCleanupRepository = databaseCleanupRepository;
        _logger = logger;

        _chatCleanupEnabled = databaseCleanupConfigurations.ChatCleanup.Enabled;
        _chatCleanupRetention = databaseCleanupConfigurations.ChatCleanup.Retention ?? TimeSpan.Zero;
        _chatCleanupTargetTime = databaseCleanupConfigurations.ChatCleanup.TargetTime ?? TimeSpan.Zero;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {ClassName} jobs", nameof(DatabaseCleanupBackgroundService));

        var cleanUpJobTask = GetExecuteTask(_chatCleanupEnabled, _chatCleanupTargetTime, RunChatCleanupJobAsync, stoppingToken);

        await Task.WhenAll(cleanUpJobTask);
    }

    internal static TimeSpan CalculateDelayToNextOccurrence(DateTime now, TimeSpan targetTime)
    {
        var nextRun = now.Date + targetTime;

        if (now >= nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private Task GetExecuteTask(bool cleanUpEnabled, TimeSpan cleanUpTargetTime, Func<CancellationToken, Task> jobMethod, CancellationToken stoppingToken)
    {
        var jobName = jobMethod.GetMethodInfo().Name;

        if (cleanUpEnabled)
        {
            _logger.LogInformation("{JobName} is enabled and will run at {TargetTime}", jobName, cleanUpTargetTime);
            return RunDelayedJobAsync(cleanUpTargetTime, jobMethod, stoppingToken);
        }

        _logger.LogInformation("{JobName} is disabled", jobName);
        return Task.CompletedTask;
    }

    private async Task RunDelayedJobAsync(TimeSpan timeTarget, Func<CancellationToken, Task> jobMethod, CancellationToken stoppingToken)
    {
        var jobName = jobMethod.GetMethodInfo().Name;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CalculateDelayToNextOccurrence(DateTime.Now, timeTarget);

            try
            {
                _logger.LogInformation("Starting {JobName} task delay of {Delay}", jobName, delay);
                await Task.Delay(delay, stoppingToken);

                await jobMethod(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Task {JobName} was requested to be canceled", jobName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Task {JobName} was canceled for some reason. {ClassName} will not run any more background jobs", jobName, nameof(DatabaseCleanupBackgroundService));
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occured while executing task {JobName}", jobName);
            }
        }
    }

    private async Task RunChatCleanupJobAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();
        _logger.LogInformation("{MethodName} job called", nameof(RunChatCleanupJobAsync));

        await _databaseCleanupRepository.CleanupChats(_chatCleanupRetention, stoppingToken);

        _logger.LogInformation("{MethodName} job finished", nameof(RunChatCleanupJobAsync));
    }
}

using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services.Backup;

/// <summary>
/// Every half minute: which enabled schedule is due? Due schedules run one after the other through <see cref="BackupService"/> (one gate for every backup and restore).
/// Errors never end the loop. At the start a run that was left <c>Running</c> by a crash is marked failed, and leftover temp files are removed.
/// </summary>
public sealed class BackupScheduler(IServiceScopeFactory scopes, BackupService backups, AppPaths paths, ILogger<BackupScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanUpAfterCrashAsync(stoppingToken);

        // give the app a moment to finish starting, then look
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "The backup scheduler could not do its round; it tries again in a moment.");
                }

                await Task.Delay(Tick, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // the app is stopping
        }
    }

    /// <summary>Runs every schedule that is due, one at a time. Public for the tests of the pieces; the loop calls it.</summary>
    public async Task RunDueAsync(CancellationToken ct)
    {
        List<ScheduleState> due;
        using (var scope = scopes.CreateScope())
        {
            var status = scope.ServiceProvider.GetRequiredService<BackupStatusService>();
            var now = DateTime.UtcNow;
            due = (await status.GetAsync(ct)).Where(s => s.Schedule.Enabled && s.NextRunUtc is { } next && next <= now).ToList();
        }

        foreach (var state in due)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("The schedule {Schedule} is due; the backup starts.", state.Schedule.Name);
            var outcome = await backups.RunAsync(state.Schedule.Id, "scheduled", ct);
            if (outcome.Succeeded)
            {
                logger.LogInformation("The scheduled backup {Schedule} is done: {File}.", state.Schedule.Name, outcome.FileName);
            }
            else
            {
                logger.LogWarning("The scheduled backup {Schedule} failed: {Message}", state.Schedule.Name, outcome.Message);
            }
        }
    }

    private async Task CleanUpAfterCrashAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var fixedRuns = await db.BackupRuns.Where(r => r.Status == BackupStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, BackupStatus.Failed).SetProperty(r => r.EndDate, now).SetProperty(r => r.Message, "The app stopped while this backup was running."), ct);
            if (fixedRuns > 0)
            {
                logger.LogWarning("{Count} backup runs were left running by a crash and are marked failed.", fixedRuns);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The history of the backups could not be checked at the start.");
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(paths.Temp).Where(f => Path.GetFileName(f).StartsWith("backup-", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("dump-", StringComparison.Ordinal)
                         || Path.GetFileName(f).StartsWith("restore-", StringComparison.Ordinal)))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Leftover files of backups in the temp folder could not be removed.");
        }
    }
}

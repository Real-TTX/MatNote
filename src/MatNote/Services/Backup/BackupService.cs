using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services.Backup;

public sealed record RunOutcome(bool Succeeded, string Message, string? FileName, long Size, int Removed);

/// <summary>
/// Makes backups: dump, zip, send to the target, apply the retention, write the <c>BackupRun</c> row. There is ONE gate: never two backups at once, and no backup while a restore runs
/// (a scheduled one, a manual one and a restore all take it). A manual run starts in the background and the page shows it in the history.
/// </summary>
public sealed class BackupService(
    IServiceScopeFactory scopes,
    ConfigStore<BackupConfig> config,
    BackupTargetFactory targets,
    AppPaths paths,
    DatabaseInfo database,
    Clock clock,
    IHostApplicationLifetime lifetime,
    ILogger<BackupService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The name of the schedule that is running now, for the pages.</summary>
    public string? Current { get; private set; }

    public bool IsBusy => _gate.CurrentCount == 0;

    /// <summary>Takes the gate for a restore; <c>null</c> when a backup (or another restore) is running.</summary>
    internal IDisposable? TryTakeGate(string what)
    {
        if (!_gate.Wait(0))
        {
            return null;
        }

        Current = what;
        return new Release(this);
    }

    private sealed class Release(BackupService owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                owner.Current = null;
                owner._gate.Release();
            }
        }
    }

    /// <summary>Runs a backup of the schedule and waits for it (the scheduler). Waits when another backup is running.</summary>
    public async Task<RunOutcome> RunAsync(string scheduleId, string trigger, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var schedule = config.Value.Schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule is null)
            {
                return new RunOutcome(false, "The schedule does not exist (any more).", null, 0, 0);
            }

            Current = schedule.Name;
            return await RunCoreAsync(schedule, trigger, ct);
        }
        finally
        {
            Current = null;
            _gate.Release();
        }
    }

    /// <summary>Starts a backup in the background and returns at once (a manual run: the request must not wait for a large dump). <c>false</c> says why not.</summary>
    public bool TryStartInBackground(string scheduleId, string trigger, out string message)
    {
        var schedule = config.Value.Schedules.FirstOrDefault(s => s.Id == scheduleId);
        if (schedule is null)
        {
            message = "The schedule does not exist.";
            return false;
        }

        if (!_gate.Wait(0))
        {
            message = $"Another backup is running right now ({Current}). Try again when it is done.";
            return false;
        }

        Current = schedule.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunCoreAsync(schedule, trigger, lifetime.ApplicationStopping);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The backup {Schedule} failed unexpectedly.", schedule.Name);
            }
            finally
            {
                Current = null;
                _gate.Release();
            }
        });
        message = $"The backup of “{schedule.Name}” has started. It appears in the history below when it is done.";
        return true;
    }

    /// <summary>The safety backup of a restore (same steps, no schedule): to the target, keys included. The caller holds the gate.</summary>
    internal Task<RunOutcome> RunSafetyAsync(BackupTarget target, CancellationToken ct)
    {
        var schedule = new BackupSchedule
        {
            Id = BackupFileName.PreRestoreId,
            Name = "Before a restore",
            TargetId = target.Id,
            IncludeKeys = true,
            Retention = new BackupRetention { KeepLast = 0, KeepDaily = 0, KeepWeekly = 0, KeepMonthly = 0 },
        };
        return RunCoreAsync(schedule, "manual", ct);
    }

    /// <summary>The work of one run. The caller holds the gate.</summary>
    private async Task<RunOutcome> RunCoreAsync(BackupSchedule schedule, string trigger, CancellationToken ct)
    {
        var target = config.Value.Targets.FirstOrDefault(t => t.Id == schedule.TargetId);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = new BackupRun
        {
            ScheduleId = schedule.Id,
            ScheduleName = schedule.Name,
            TargetName = target?.Name ?? "(deleted target)",
            Trigger = trigger,
            StartDate = DateTime.UtcNow,
            Status = BackupStatus.Running,
        };
        db.BackupRuns.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        var zipPath = Path.Combine(paths.Temp, "backup-" + Guid.NewGuid().ToString("N") + ".zip");
        var outcome = new RunOutcome(false, "The backup did not finish.", null, 0, 0);
        try
        {
            if (target is null)
            {
                throw new BackupTargetException("The target of this schedule does not exist (any more). Choose another target in the schedule.");
            }

            var destination = targets.Create(target);
            var created = DateTime.UtcNow;
            created = new DateTime(created.Ticks - created.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            var name = BackupFileName.Create(schedule.Id, created);

            await BackupArchive.BuildAsync(zipPath, paths.Temp, schedule, created, paths, PgConnection.Parse(database.ConnectionString), db, ct);
            var size = new FileInfo(zipPath).Length;
            await using (var file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await destination.UploadAsync(name, file, ct);
            }

            var (removed, warning) = await ApplyRetentionAsync(destination, schedule, name, ct);
            outcome = new RunOutcome(true, warning ?? string.Empty, name, size, removed);
        }
        catch (OperationCanceledException)
        {
            outcome = new RunOutcome(false, "The app was stopped while the backup was running.", null, 0, 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The backup {Schedule} failed.", schedule.Name);
            outcome = new RunOutcome(false, ex.Message, null, 0, 0);
        }
        finally
        {
            TryDelete(zipPath);
        }

        try
        {
            run.Status = outcome.Succeeded ? BackupStatus.Succeeded : BackupStatus.Failed;
            run.EndDate = DateTime.UtcNow;
            run.Message = outcome.Message.Length == 0 ? null : outcome.Message[..Math.Min(outcome.Message.Length, 2000)];
            run.FileName = outcome.FileName;
            run.SizeBytes = outcome.Size;
            run.Removed = outcome.Removed;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The result of the backup {Schedule} could not be written to the history.", schedule.Name);
        }

        return outcome;
    }

    /// <summary>Deletes what the retention of the schedule no longer keeps. A file that cannot be deleted is no reason to fail a backup that is made: it is reported in the message.</summary>
    private async Task<(int Removed, string? Warning)> ApplyRetentionAsync(IBackupTarget destination, BackupSchedule schedule, string justMade, CancellationToken ct)
    {
        if (schedule.Retention.KeepsEverything)
        {
            return (0, null);
        }

        try
        {
            var files = await destination.ListAsync(ct);
            var doomed = Retention.ToDeleteForSchedule(files.Select(f => f.Name), schedule.Id, schedule.Retention, justMade, clock.Zone);
            var removed = 0;
            string? warning = null;
            foreach (var name in doomed)
            {
                try
                {
                    await destination.DeleteAsync(name, ct);
                    removed++;
                }
                catch (BackupTargetException ex)
                {
                    warning ??= $"The backup was made, but an old one could not be removed: {ex.Message}";
                }
            }

            return (removed, warning);
        }
        catch (BackupTargetException ex)
        {
            return (0, $"The backup was made, but the old ones could not be checked: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cleanup of the temp folder at the next start takes it
        }
    }
}

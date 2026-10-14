using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services.Backup;

/// <summary>What the pages (and the scheduler) know about a schedule: when it runs next, and how its last run went.</summary>
public sealed record ScheduleState(BackupSchedule Schedule, string TargetName, DateTime? NextRunUtc, BackupRun? LastRun, bool HasSuccess);

/// <summary>
/// The state of the schedules. The next run is counted from the later of the last run (a failed or a manual one counts, so a failing schedule does not try again every
/// half minute) and the moment the schedule was last changed (so a slot that lay before the change is not made up for). A slot that was missed while the app was off
/// therefore runs ONCE at the start, and then the schedule is back in its rhythm.
/// </summary>
public sealed class BackupStatusService(AppDbContext db, ConfigStore<BackupConfig> config, Clock clock)
{
    public async Task<List<ScheduleState>> GetAsync(CancellationToken ct = default)
    {
        var current = config.Value;
        var zone = clock.Zone;
        var result = new List<ScheduleState>();
        foreach (var schedule in current.Schedules)
        {
            var last = await db.BackupRuns.AsNoTracking().Where(r => r.ScheduleId == schedule.Id).OrderByDescending(r => r.StartDate).FirstOrDefaultAsync(ct);
            var success = await db.BackupRuns.AsNoTracking().AnyAsync(r => r.ScheduleId == schedule.Id && r.Status == BackupStatus.Succeeded, ct);
            var baseline = last is not null && last.StartDate > schedule.Modified ? last.StartDate : schedule.Modified;
            DateTime? next = schedule.Enabled ? ScheduleCalculator.NextRun(schedule, baseline, zone) : null;
            var target = current.Targets.FirstOrDefault(t => t.Id == schedule.TargetId)?.Name ?? "(no target)";
            result.Add(new ScheduleState(schedule, target, next, last, success));
        }

        return result;
    }
}

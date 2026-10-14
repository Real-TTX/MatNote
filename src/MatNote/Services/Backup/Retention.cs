using System.Globalization;

namespace MatNote.Services.Backup;

/// <summary>
/// The retention of backups, as a pure function (no clock, no file system, nothing to mock). A backup is kept when ANY rule keeps it: the newest N
/// (<see cref="BackupRetention.KeepLast"/>), the newest of each of the last N days, ISO weeks or months <em>that have a backup</em>. A rule at 0 is off; all rules at 0 keep everything.
/// Periods are counted from the backups that exist, not from today: a pause (the app was off for a month) never makes the rules delete everything that is old.
/// </summary>
public static class Retention
{
    /// <summary>
    /// The names to delete. <paramref name="protect"/> (the backup that was just made) is never among them. A period starts at midnight of <paramref name="zone"/> (default UTC).
    /// </summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<(string Name, DateTime Utc)> backups, BackupRetention rules, string? protect = null, TimeZoneInfo? zone = null)
    {
        var ordered = backups.OrderByDescending(b => b.Utc).ThenByDescending(b => b.Name, StringComparer.Ordinal).ToList();
        if (rules.KeepsEverything)
        {
            return [];
        }

        zone ??= TimeZoneInfo.Utc;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (protect is not null)
        {
            keep.Add(protect);
        }

        foreach (var b in ordered.Take(Math.Max(0, rules.KeepLast)))
        {
            keep.Add(b.Name);
        }

        KeepNewestPerPeriod(ordered, rules.KeepDaily, zone, local => (local.Year, local.DayOfYear, 0), keep);
        KeepNewestPerPeriod(ordered, rules.KeepWeekly, zone, local => (ISOWeek.GetYear(local), ISOWeek.GetWeekOfYear(local), 0), keep);
        KeepNewestPerPeriod(ordered, rules.KeepMonthly, zone, local => (local.Year, local.Month, 0), keep);

        return ordered.Where(b => !keep.Contains(b.Name)).Select(b => b.Name).ToList();
    }

    /// <summary>
    /// The names of one schedule to delete, out of every name that was found at the target: names of other schedules (and files that are not backups at all) are never returned.
    /// </summary>
    public static IReadOnlyList<string> ToDeleteForSchedule(IEnumerable<string> fileNames, string scheduleId, BackupRetention rules, string? protect = null, TimeZoneInfo? zone = null)
    {
        var mine = new List<(string Name, DateTime Utc)>();
        foreach (var name in fileNames)
        {
            if (BackupFileName.TryParse(name, out var id, out var utc) && id == scheduleId)
            {
                mine.Add((name, utc));
            }
        }

        return ToDelete(mine, rules, protect, zone);
    }

    private static void KeepNewestPerPeriod(List<(string Name, DateTime Utc)> newestFirst, int count, TimeZoneInfo zone, Func<DateTime, (int, int, int)> period, HashSet<string> keep)
    {
        if (count <= 0)
        {
            return;
        }

        var seen = new HashSet<(int, int, int)>();
        foreach (var b in newestFirst)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.Utc, DateTimeKind.Utc), zone);
            if (seen.Add(period(local)))
            {
                keep.Add(b.Name);
                if (seen.Count >= count)
                {
                    return;
                }
            }
        }
    }
}

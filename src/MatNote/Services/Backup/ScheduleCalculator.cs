using System.Globalization;
using Cronos;

namespace MatNote.Services.Backup;

/// <summary>
/// Turns the friendly fields of a schedule into a cron expression and says when it runs next, in the time zone of the app (so "02:30" stays 02:30 when the clock changes).
/// Pure: it knows no clock and no database.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>The choices of "every N hours": divisors of 24, so that the rhythm is the same every day.</summary>
    public static readonly int[] HourSteps = [1, 2, 3, 4, 6, 8, 12];

    public static bool TryParseTime(string? text, out int hour, out int minute)
    {
        hour = minute = 0;
        var parts = (text ?? string.Empty).Trim().Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour) && hour is >= 0 and <= 23
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute) && minute is >= 0 and <= 59;
    }

    /// <summary>The cron expression (five fields) of a schedule, or <c>null</c> when its fields do not make a valid one.</summary>
    public static string? ToCron(BackupSchedule s)
    {
        switch (s.Frequency)
        {
            case BackupFrequency.Hourly:
                return s.Minute is >= 0 and <= 59 && HourSteps.Contains(s.EveryHours)
                    ? string.Create(CultureInfo.InvariantCulture, $"{s.Minute} {(s.EveryHours == 1 ? "*" : "*/" + s.EveryHours)} * * *")
                    : null;
            case BackupFrequency.Daily:
                return TryParseTime(s.Time, out var h, out var m) ? string.Create(CultureInfo.InvariantCulture, $"{m} {h} * * *") : null;
            case BackupFrequency.Weekly:
                return TryParseTime(s.Time, out h, out m) && Enum.IsDefined(s.DayOfWeek)
                    ? string.Create(CultureInfo.InvariantCulture, $"{m} {h} * * {(int)s.DayOfWeek}")
                    : null;
            case BackupFrequency.Monthly:
                return TryParseTime(s.Time, out h, out m) && s.DayOfMonth is >= 0 and <= 28
                    ? string.Create(CultureInfo.InvariantCulture, $"{m} {h} {(s.DayOfMonth == 0 ? "L" : s.DayOfMonth.ToString(CultureInfo.InvariantCulture))} * *")
                    : null;
            case BackupFrequency.Cron:
                var text = (s.Cron ?? string.Empty).Trim();
                return CronExpression.TryParse(text, CronFormat.Standard, out _) ? text : null;
            default:
                return null;
        }
    }

    /// <summary>The first run after <paramref name="afterUtc"/> (UTC), or <c>null</c> when the schedule has no valid expression or no further run.</summary>
    public static DateTime? NextRun(BackupSchedule s, DateTime afterUtc, TimeZoneInfo zone)
    {
        var cron = ToCron(s);
        if (cron is null)
        {
            return null;
        }

        var from = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        return CronExpression.Parse(cron, CronFormat.Standard).GetNextOccurrence(from, zone, inclusive: false);
    }

    /// <summary>What is wrong with the fields of a schedule, as messages for the form (field name, text); empty when it is fine.</summary>
    public static IReadOnlyList<(string Field, string Message)> Validate(BackupSchedule s)
    {
        var errors = new List<(string, string)>();
        switch (s.Frequency)
        {
            case BackupFrequency.Hourly:
                if (s.Minute is < 0 or > 59)
                {
                    errors.Add((nameof(s.Minute), "The minute is between 0 and 59."));
                }

                if (!HourSteps.Contains(s.EveryHours))
                {
                    errors.Add((nameof(s.EveryHours), "Please choose how often."));
                }

                break;
            case BackupFrequency.Daily:
            case BackupFrequency.Weekly:
            case BackupFrequency.Monthly:
                if (!TryParseTime(s.Time, out _, out _))
                {
                    errors.Add((nameof(s.Time), "Please choose a time of day."));
                }

                if (s.Frequency == BackupFrequency.Monthly && s.DayOfMonth is < 0 or > 28)
                {
                    errors.Add((nameof(s.DayOfMonth), "Please choose a day of the month."));
                }

                break;
            case BackupFrequency.Cron:
                if (string.IsNullOrWhiteSpace(s.Cron))
                {
                    errors.Add((nameof(s.Cron), "Please enter a cron expression."));
                }
                else if (!CronExpression.TryParse(s.Cron.Trim(), CronFormat.Standard, out _))
                {
                    errors.Add((nameof(s.Cron), "This is not a valid cron expression. It has five fields: minute, hour, day of the month, month, day of the week."));
                }

                break;
        }

        return errors;
    }

    /// <summary>The schedule in words: "Every day at 02:30".</summary>
    public static string Describe(BackupSchedule s) => s.Frequency switch
    {
        BackupFrequency.Hourly => s.EveryHours <= 1
            ? string.Create(CultureInfo.InvariantCulture, $"Every hour at minute {s.Minute}")
            : string.Create(CultureInfo.InvariantCulture, $"Every {s.EveryHours} hours at minute {s.Minute}"),
        BackupFrequency.Daily => $"Every day at {s.Time}",
        BackupFrequency.Weekly => $"Every {s.DayOfWeek} at {s.Time}",
        BackupFrequency.Monthly => s.DayOfMonth == 0 ? $"On the last day of every month at {s.Time}" : $"On day {s.DayOfMonth} of every month at {s.Time}",
        _ => $"Cron: {s.Cron}",
    };

    /// <summary>The rules of the retention in words: "Last 7, 7 daily, 4 weekly, 6 monthly", or "Keep everything".</summary>
    public static string DescribeRetention(BackupRetention r)
    {
        if (r.KeepsEverything)
        {
            return "Keep everything";
        }

        var parts = new List<string>();
        if (r.KeepLast > 0)
        {
            parts.Add($"last {r.KeepLast}");
        }

        if (r.KeepDaily > 0)
        {
            parts.Add($"{r.KeepDaily} daily");
        }

        if (r.KeepWeekly > 0)
        {
            parts.Add($"{r.KeepWeekly} weekly");
        }

        if (r.KeepMonthly > 0)
        {
            parts.Add($"{r.KeepMonthly} monthly");
        }

        var text = string.Join(", ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}

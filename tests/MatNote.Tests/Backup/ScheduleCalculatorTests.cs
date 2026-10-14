using MatNote.Services.Backup;

namespace MatNote.Tests.Backup;

/// <summary>Time zones by IANA name on Linux, by Windows name on a developer PC (the tests must run on both).</summary>
internal static class TestZones
{
    public static TimeZoneInfo Berlin()
    {
        foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try the next name
            }
        }

        throw new TimeZoneNotFoundException("No time zone for Berlin on this machine.");
    }
}

public class ScheduleCalculatorTests
{
    private static BackupSchedule Schedule(BackupFrequency frequency, Action<BackupSchedule>? change = null)
    {
        var s = new BackupSchedule { Id = "test0001", Name = "Test", Frequency = frequency };
        change?.Invoke(s);
        return s;
    }

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(BackupFrequency.Daily, "02:30", "30 2 * * *")]
    [InlineData(BackupFrequency.Daily, "0:05", "5 0 * * *")]
    [InlineData(BackupFrequency.Daily, "23:59", "59 23 * * *")]
    public void Friendly_daily_becomes_cron(BackupFrequency frequency, string time, string expected)
    {
        Assert.Equal(expected, ScheduleCalculator.ToCron(Schedule(frequency, s => s.Time = time)));
    }

    [Fact]
    public void Friendly_hourly_becomes_cron()
    {
        Assert.Equal("15 * * * *", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Hourly, s => { s.Minute = 15; s.EveryHours = 1; })));
        Assert.Equal("0 */4 * * *", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Hourly, s => { s.Minute = 0; s.EveryHours = 4; })));
    }

    [Fact]
    public void Friendly_weekly_and_monthly_become_cron()
    {
        Assert.Equal("0 3 * * 0", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Weekly, s => { s.Time = "03:00"; s.DayOfWeek = DayOfWeek.Sunday; })));
        Assert.Equal("30 2 * * 5", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Weekly, s => { s.Time = "02:30"; s.DayOfWeek = DayOfWeek.Friday; })));
        Assert.Equal("0 4 15 * *", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Monthly, s => { s.Time = "04:00"; s.DayOfMonth = 15; })));
        Assert.Equal("0 4 L * *", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Monthly, s => { s.Time = "04:00"; s.DayOfMonth = 0; })));
    }

    [Fact]
    public void Cron_is_taken_as_it_is_when_it_is_valid()
    {
        Assert.Equal("0 3 * * 1-5", ScheduleCalculator.ToCron(Schedule(BackupFrequency.Cron, s => s.Cron = "  0 3 * * 1-5 ")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("61 * * * *")]
    [InlineData("* * * *")]
    public void An_invalid_cron_has_no_expression(string cron)
    {
        var s = Schedule(BackupFrequency.Cron, x => x.Cron = cron);

        Assert.Null(ScheduleCalculator.ToCron(s));
        Assert.Contains(ScheduleCalculator.Validate(s), e => e.Field == nameof(BackupSchedule.Cron));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("2:60")]
    [InlineData("noon")]
    [InlineData("")]
    public void An_invalid_time_has_no_expression(string time)
    {
        var s = Schedule(BackupFrequency.Daily, x => x.Time = time);

        Assert.Null(ScheduleCalculator.ToCron(s));
        Assert.Contains(ScheduleCalculator.Validate(s), e => e.Field == nameof(BackupSchedule.Time));
    }

    [Fact]
    public void Hourly_every_five_hours_is_refused_because_it_would_not_repeat_daily()
    {
        var s = Schedule(BackupFrequency.Hourly, x => x.EveryHours = 5);

        Assert.Null(ScheduleCalculator.ToCron(s));
        Assert.Contains(ScheduleCalculator.Validate(s), e => e.Field == nameof(BackupSchedule.EveryHours));
    }

    [Fact]
    public void A_valid_schedule_has_no_errors()
    {
        Assert.Empty(ScheduleCalculator.Validate(Schedule(BackupFrequency.Daily)));
        Assert.Empty(ScheduleCalculator.Validate(Schedule(BackupFrequency.Weekly)));
        Assert.Empty(ScheduleCalculator.Validate(Schedule(BackupFrequency.Monthly)));
        Assert.Empty(ScheduleCalculator.Validate(Schedule(BackupFrequency.Cron)));
        Assert.Empty(ScheduleCalculator.Validate(Schedule(BackupFrequency.Hourly)));
    }

    [Fact]
    public void Daily_runs_at_the_local_time_and_is_returned_in_utc()
    {
        var berlin = TestZones.Berlin();
        var s = Schedule(BackupFrequency.Daily, x => x.Time = "02:30");

        // in October Berlin is UTC+2: 02:30 local is 00:30 UTC
        Assert.Equal(Utc(2026, 10, 15, 0, 30), ScheduleCalculator.NextRun(s, Utc(2026, 10, 14, 12, 0), berlin));
        Assert.Equal(Utc(2026, 10, 14, 0, 30), ScheduleCalculator.NextRun(s, Utc(2026, 10, 13, 12, 0), berlin));
    }

    [Fact]
    public void Daily_stays_at_the_local_time_when_the_clocks_change()
    {
        var berlin = TestZones.Berlin();
        var s = Schedule(BackupFrequency.Daily, x => x.Time = "02:30");

        // 2026-10-25 the clocks go back (UTC+2 to UTC+1): from then on 02:30 local is 01:30 UTC
        Assert.Equal(Utc(2026, 10, 27, 1, 30), ScheduleCalculator.NextRun(s, Utc(2026, 10, 26, 12, 0), berlin));
        // and in summer it is 00:30 UTC again
        Assert.Equal(Utc(2027, 7, 2, 0, 30), ScheduleCalculator.NextRun(s, Utc(2027, 7, 1, 12, 0), berlin));
    }

    [Fact]
    public void The_next_run_is_always_after_the_given_moment()
    {
        var s = Schedule(BackupFrequency.Daily, x => x.Time = "02:30");
        var slot = Utc(2026, 10, 14, 2, 30);

        Assert.Equal(Utc(2026, 10, 15, 2, 30), ScheduleCalculator.NextRun(s, slot, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Weekly_finds_the_right_weekday()
    {
        var s = Schedule(BackupFrequency.Weekly, x => { x.Time = "03:00"; x.DayOfWeek = DayOfWeek.Monday; });

        // 2026-10-14 is a Wednesday: the next Monday is the 19th
        Assert.Equal(Utc(2026, 10, 19, 3, 0), ScheduleCalculator.NextRun(s, Utc(2026, 10, 14, 10, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Monthly_on_the_last_day_follows_the_length_of_the_month()
    {
        var s = Schedule(BackupFrequency.Monthly, x => { x.Time = "01:00"; x.DayOfMonth = 0; });

        Assert.Equal(Utc(2027, 2, 28, 1, 0), ScheduleCalculator.NextRun(s, Utc(2027, 2, 1), TimeZoneInfo.Utc));
        Assert.Equal(Utc(2028, 2, 29, 1, 0), ScheduleCalculator.NextRun(s, Utc(2028, 2, 1), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Hourly_every_four_hours()
    {
        var s = Schedule(BackupFrequency.Hourly, x => { x.Minute = 10; x.EveryHours = 4; });

        Assert.Equal(Utc(2026, 10, 14, 8, 10), ScheduleCalculator.NextRun(s, Utc(2026, 10, 14, 4, 10), TimeZoneInfo.Utc));
        Assert.Equal(Utc(2026, 10, 15, 0, 10), ScheduleCalculator.NextRun(s, Utc(2026, 10, 14, 20, 10), TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_schedule_without_a_valid_expression_never_runs()
    {
        Assert.Null(ScheduleCalculator.NextRun(Schedule(BackupFrequency.Cron, x => x.Cron = "nope"), Utc(2026, 10, 14), TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_missed_slot_is_due_once_and_then_the_schedule_is_back_in_its_rhythm()
    {
        // the app was off for three days: counted from the last run the next slot lies in the past ...
        var s = Schedule(BackupFrequency.Daily, x => x.Time = "02:30");
        var lastRun = Utc(2026, 10, 11, 2, 30);
        var now = Utc(2026, 10, 14, 9, 0);
        var due = ScheduleCalculator.NextRun(s, lastRun, TimeZoneInfo.Utc);
        Assert.True(due <= now);

        // ... and after that one run (started now) the next slot is in the future: the two missed days are not made up
        var afterRun = ScheduleCalculator.NextRun(s, now, TimeZoneInfo.Utc);
        Assert.True(afterRun > now);
        Assert.Equal(Utc(2026, 10, 15, 2, 30), afterRun);
    }

    [Fact]
    public void Descriptions_are_readable()
    {
        Assert.Equal("Every day at 02:30", ScheduleCalculator.Describe(Schedule(BackupFrequency.Daily)));
        Assert.Equal("Every Monday at 02:30", ScheduleCalculator.Describe(Schedule(BackupFrequency.Weekly)));
        Assert.Equal("Every 4 hours at minute 10", ScheduleCalculator.Describe(Schedule(BackupFrequency.Hourly, x => { x.EveryHours = 4; x.Minute = 10; })));
        Assert.Equal("Every hour at minute 0", ScheduleCalculator.Describe(Schedule(BackupFrequency.Hourly)));
        Assert.Equal("On the last day of every month at 02:30", ScheduleCalculator.Describe(Schedule(BackupFrequency.Monthly, x => x.DayOfMonth = 0)));
        Assert.Equal("Last 7, 7 daily, 4 weekly, 6 monthly", ScheduleCalculator.DescribeRetention(new BackupRetention()));
        Assert.Equal("Keep everything", ScheduleCalculator.DescribeRetention(new BackupRetention { KeepLast = 0, KeepDaily = 0, KeepWeekly = 0, KeepMonthly = 0 }));
    }
}

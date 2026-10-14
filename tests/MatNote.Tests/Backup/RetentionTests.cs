using MatNote.Services.Backup;

namespace MatNote.Tests.Backup;

public class RetentionTests
{
    private static (string Name, DateTime Utc) At(DateTime utc, string schedule = "aaaa1111") => (BackupFileName.Create(schedule, utc), utc);

    private static BackupRetention Rules(int last = 0, int daily = 0, int weekly = 0, int monthly = 0)
        => new() { KeepLast = last, KeepDaily = daily, KeepWeekly = weekly, KeepMonthly = monthly };

    /// <summary>One backup per day at 02:30, <paramref name="days"/> days, the newest on 2026-10-14.</summary>
    private static List<(string Name, DateTime Utc)> Daily(int days, string schedule = "aaaa1111")
        => Enumerable.Range(0, days).Select(i => At(new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc).AddDays(-i), schedule)).ToList();

    [Fact]
    public void All_rules_at_zero_keep_everything()
    {
        Assert.Empty(Retention.ToDelete(Daily(40), Rules()));
    }

    [Fact]
    public void Negative_values_are_the_same_as_zero()
    {
        Assert.Empty(Retention.ToDelete(Daily(10), Rules(-1, -5, -2, -3)));
    }

    [Fact]
    public void Keep_last_keeps_the_newest_N()
    {
        var all = Daily(10);
        var doomed = Retention.ToDelete(all, Rules(last: 3));

        Assert.Equal(7, doomed.Count);
        Assert.DoesNotContain(all[0].Name, doomed);
        Assert.DoesNotContain(all[1].Name, doomed);
        Assert.DoesNotContain(all[2].Name, doomed);
        Assert.Contains(all[3].Name, doomed);
    }

    [Fact]
    public void Keep_last_more_than_there_are_deletes_nothing()
    {
        Assert.Empty(Retention.ToDelete(Daily(4), Rules(last: 10)));
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var all = Daily(6);
        var shuffled = all.OrderBy(b => b.Name.GetHashCode() ^ 0x5f3759df).ToList();

        Assert.Equal(Retention.ToDelete(all, Rules(last: 2)).OrderBy(n => n), Retention.ToDelete(shuffled, Rules(last: 2)).OrderBy(n => n));
    }

    [Fact]
    public void Daily_keeps_the_newest_backup_of_each_day()
    {
        // three backups on the 14th, two on the 13th, one on the 12th
        var a = At(new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        var b = At(new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc));
        var c = At(new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc));
        var d = At(new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc));
        var e = At(new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc));
        var f = At(new DateTime(2026, 10, 12, 4, 0, 0, DateTimeKind.Utc));

        var doomed = Retention.ToDelete([a, b, c, d, e, f], Rules(daily: 2));

        Assert.Equal(new[] { a.Name, b.Name, d.Name, f.Name }.OrderBy(n => n), doomed.OrderBy(n => n));
    }

    [Fact]
    public void Daily_counts_days_that_have_a_backup_so_a_pause_does_not_delete_everything()
    {
        // a month without backups between the two
        var old = At(new DateTime(2026, 8, 10, 2, 30, 0, DateTimeKind.Utc));
        var recent = At(new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc));

        Assert.Empty(Retention.ToDelete([old, recent], Rules(daily: 2)));
    }

    [Fact]
    public void Weekly_uses_iso_weeks_that_start_on_monday()
    {
        // 2026-10-11 is a Sunday (week 41), 2026-10-12 a Monday (week 42)
        var sunday = At(new DateTime(2026, 10, 11, 23, 0, 0, DateTimeKind.Utc));
        var mondayEarly = At(new DateTime(2026, 10, 12, 0, 30, 0, DateTimeKind.Utc));
        var wednesday = At(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));

        var doomed = Retention.ToDelete([sunday, mondayEarly, wednesday], Rules(weekly: 2));

        // week 42 keeps its newest (Wednesday), week 41 keeps its only one (Sunday): the Monday backup goes
        Assert.Equal([mondayEarly.Name], doomed);
    }

    [Fact]
    public void Weekly_handles_the_turn_of_the_year()
    {
        // 2026-12-31 (Thursday) and 2027-01-01 (Friday) are both ISO week 53 of 2026
        var a = At(new DateTime(2026, 12, 31, 10, 0, 0, DateTimeKind.Utc));
        var b = At(new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var c = At(new DateTime(2026, 12, 24, 10, 0, 0, DateTimeKind.Utc));

        var doomed = Retention.ToDelete([a, b, c], Rules(weekly: 5));

        Assert.Equal([a.Name], doomed);
    }

    [Fact]
    public void Monthly_keeps_the_newest_backup_of_each_month()
    {
        var oct2 = At(new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc));
        var oct20 = At(new DateTime(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc));
        var sep5 = At(new DateTime(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc));
        var sep28 = At(new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc));
        var aug1 = At(new DateTime(2026, 8, 1, 2, 0, 0, DateTimeKind.Utc));

        var doomed = Retention.ToDelete([oct2, oct20, sep5, sep28, aug1], Rules(monthly: 2));

        Assert.Equal(new[] { oct2.Name, sep5.Name, aug1.Name }.OrderBy(n => n), doomed.OrderBy(n => n));
    }

    [Fact]
    public void A_backup_is_kept_when_any_rule_keeps_it()
    {
        var all = Daily(60);

        var doomed = Retention.ToDelete(all, Rules(last: 3, daily: 5, weekly: 4, monthly: 2)).ToHashSet();
        var kept = all.Where(b => !doomed.Contains(b.Name)).ToList();

        // newest 3 and the newest 5 days are the same five backups; the weeks and the months add more
        Assert.True(kept.Count > 5);
        Assert.All(all.Take(5), b => Assert.DoesNotContain(b.Name, doomed));
        // the oldest backup is in no rule
        Assert.Contains(all[^1].Name, doomed);
        // 4 weekly: the newest of each of the 4 newest ISO weeks is kept (2026-10-14 is a Wednesday)
        Assert.DoesNotContain(At(new DateTime(2026, 9, 27, 2, 30, 0, DateTimeKind.Utc)).Name, doomed); // the Sunday that ends week 39
        // 2 monthly: the newest of October (the 14th) and of September (the 30th)
        Assert.DoesNotContain(At(new DateTime(2026, 9, 30, 2, 30, 0, DateTimeKind.Utc)).Name, doomed);
    }

    [Fact]
    public void Rules_that_overlap_do_not_keep_more_than_their_union()
    {
        var all = Daily(30);
        var doomed = Retention.ToDelete(all, Rules(last: 7, daily: 7));

        Assert.Equal(23, doomed.Count);
    }

    [Fact]
    public void The_backup_that_was_just_made_is_never_deleted()
    {
        var all = Daily(5);
        var oldest = all[^1].Name;

        // a rule set that would delete it (keep the newest 1), and the caller protects it
        var doomed = Retention.ToDelete(all, Rules(last: 1), protect: oldest);

        Assert.DoesNotContain(oldest, doomed);
        Assert.Equal(3, doomed.Count);
    }

    [Fact]
    public void The_protected_backup_is_kept_even_when_the_clock_put_it_in_the_past()
    {
        var all = Daily(5);
        var wrong = At(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        all.Add(wrong);

        Assert.DoesNotContain(wrong.Name, Retention.ToDelete(all, Rules(last: 2), protect: wrong.Name));
    }

    [Fact]
    public void Only_the_files_of_this_schedule_are_ever_returned()
    {
        var mine = Daily(8, "mine0001").Select(b => b.Name);
        var other = Daily(8, "other002").Select(b => b.Name);
        var foreign = new[] { "notes.txt", "matnote_mine0001_20261014-023000Z.zip.part", "matnote_mine0001_nonsense.zip", "../matnote_mine0001_20261014-023000Z.zip" };

        var doomed = Retention.ToDeleteForSchedule(mine.Concat(other).Concat(foreign), "mine0001", Rules(last: 2));

        Assert.Equal(6, doomed.Count);
        Assert.All(doomed, n => Assert.StartsWith("matnote_mine0001_", n));
        Assert.DoesNotContain(foreign[3], doomed);
    }

    [Fact]
    public void Another_schedule_with_a_longer_id_that_starts_the_same_is_not_confused()
    {
        var a = At(new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc), "abc");
        var b = At(new DateTime(2026, 10, 13, 2, 30, 0, DateTimeKind.Utc), "abcdef");

        Assert.Empty(Retention.ToDeleteForSchedule([a.Name, b.Name], "abc", Rules(last: 1)));
        Assert.Empty(Retention.ToDeleteForSchedule([a.Name, b.Name], "abcdef", Rules(last: 1)));
    }

    [Fact]
    public void Days_are_cut_at_midnight_of_the_zone()
    {
        // 23:30 UTC on the 13th is already the 14th in Berlin (UTC+2 in October): the two backups share a day there, not in UTC
        var late = At(new DateTime(2026, 10, 13, 23, 30, 0, DateTimeKind.Utc));
        var early = At(new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        var berlin = TestZones.Berlin();

        Assert.Empty(Retention.ToDelete([late, early], Rules(daily: 2)));
        Assert.Equal([late.Name], Retention.ToDelete([late, early], Rules(daily: 2), zone: berlin));
    }

    [Fact]
    public void Nothing_to_delete_for_an_empty_list()
    {
        Assert.Empty(Retention.ToDelete([], Rules(last: 1, daily: 1)));
    }

    [Fact]
    public void Retention_is_pure_and_repeatable()
    {
        var all = Daily(30);
        var rules = Rules(3, 3, 2, 2);

        Assert.Equal(Retention.ToDelete(all, rules), Retention.ToDelete(all, rules));
    }
}

using MatNote.Controls;
using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Admin.Backups.Schedules;

/// <summary>Create or change a schedule. Its fields depend on the frequency, so only the ones that are needed are shown (and checked). Delete is here; the backup files of a deleted schedule stay.</summary>
public sealed class EditModel(ConfigStore<BackupConfig> config, Clock clock) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public bool Enabled { get; set; } = true;

    [BindProperty] public string TargetId { get; set; } = string.Empty;

    [BindProperty] public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    [BindProperty] public string Time { get; set; } = "02:30";

    [BindProperty] public int Minute { get; set; }

    [BindProperty] public int EveryHours { get; set; } = 1;

    [BindProperty] public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    [BindProperty] public int DayOfMonth { get; set; } = 1;

    [BindProperty] public string Cron { get; set; } = "30 2 * * *";

    [BindProperty] public int KeepLast { get; set; } = 7;

    [BindProperty] public int KeepDaily { get; set; } = 7;

    [BindProperty] public int KeepWeekly { get; set; } = 4;

    [BindProperty] public int KeepMonthly { get; set; } = 6;

    [BindProperty] public bool IncludeKeys { get; set; } = true;

    public bool IsNew => Id.Length == 0;

    public IReadOnlyList<Choice> Targets => config.Value.Targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => new Choice(t.Id, t.Name)).ToList();

    public IReadOnlyList<Choice> Frequencies { get; } =
    [
        new(nameof(BackupFrequency.Hourly), "Every few hours"),
        new(nameof(BackupFrequency.Daily), "Daily"),
        new(nameof(BackupFrequency.Weekly), "Weekly"),
        new(nameof(BackupFrequency.Monthly), "Monthly"),
        new(nameof(BackupFrequency.Cron), "Custom (cron)"),
    ];

    public IReadOnlyList<Choice> HourSteps { get; } = ScheduleCalculator.HourSteps.Select(h => new Choice(h.ToString(System.Globalization.CultureInfo.InvariantCulture), h == 1 ? "Every hour" : $"Every {h} hours")).ToList();

    public IReadOnlyList<Choice> Weekdays { get; } = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
        .Select(d => new Choice(d.ToString(), d.ToString())).ToList();

    public IReadOnlyList<Choice> MonthDays { get; } = Enumerable.Range(1, 28).Select(d => new Choice(d.ToString(System.Globalization.CultureInfo.InvariantCulture), d.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        .Append(new Choice("0", "The last day")).ToList();

    public string ZoneName => clock.Zone.Id;

    public IActionResult OnGet()
    {
        if (!IsNew)
        {
            var s = Find();
            if (s is null)
            {
                return NotFound();
            }

            Name = s.Name;
            Enabled = s.Enabled;
            TargetId = s.TargetId;
            Frequency = s.Frequency;
            Time = s.Time;
            Minute = s.Minute;
            EveryHours = s.EveryHours;
            DayOfWeek = s.DayOfWeek;
            DayOfMonth = s.DayOfMonth;
            Cron = s.Cron;
            KeepLast = s.Retention.KeepLast;
            KeepDaily = s.Retention.KeepDaily;
            KeepWeekly = s.Retention.KeepWeekly;
            KeepMonthly = s.Retention.KeepMonthly;
            IncludeKeys = s.IncludeKeys;
        }
        else if (config.Value.Targets.Count == 1)
        {
            TargetId = config.Value.Targets[0].Id;
        }

        Prepare();
        return Page();
    }

    public IActionResult OnPost()
    {
        var existing = IsNew ? null : Find();
        if (!IsNew && existing is null)
        {
            return NotFound();
        }

        Name = Name.Trim();
        if (Name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), "Please enter a name.");
        }
        else if (Name.Length > 120)
        {
            ModelState.AddModelError(nameof(Name), "The name is too long (120 characters at most).");
        }
        else if (config.Value.Schedules.Any(s => s.Id != Id && string.Equals(s.Name, Name, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Name), "There is a schedule with this name already.");
        }

        if (config.Value.Targets.All(t => t.Id != TargetId))
        {
            ModelState.AddModelError(nameof(TargetId), config.Value.Targets.Count == 0 ? "There is no target yet: add one first (the Targets tab)." : "Please choose a target.");
        }

        // a number that is empty or not a number comes with a technical message: say it in words
        foreach (var field in new[] { nameof(Minute), nameof(EveryHours), nameof(DayOfMonth), nameof(KeepLast), nameof(KeepDaily), nameof(KeepWeekly), nameof(KeepMonthly) })
        {
            if (ModelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0)
            {
                entry.Errors.Clear();
                ModelState.AddModelError(field, "Please enter a whole number.");
            }
        }

        foreach (var (field, message) in ScheduleCalculator.Validate(Build(new BackupSchedule())))
        {
            if (!ModelState.ContainsKey(field) || ModelState[field]!.Errors.Count == 0)
            {
                ModelState.AddModelError(field, message);
            }
        }

        foreach (var (field, value) in new[] { (nameof(KeepLast), KeepLast), (nameof(KeepDaily), KeepDaily), (nameof(KeepWeekly), KeepWeekly), (nameof(KeepMonthly), KeepMonthly) })
        {
            if ((value < 0 || value > 10000) && !ModelState.ContainsKey(field))
            {
                ModelState.AddModelError(field, "The number is between 0 and 10000 (0 turns the rule off).");
            }
        }

        if (!ModelState.IsValid)
        {
            Prepare();
            return Page();
        }

        config.Update(c =>
        {
            var schedule = IsNew ? new BackupSchedule() : c.Schedules.First(s => s.Id == Id);
            if (IsNew)
            {
                c.Schedules.Add(schedule);
            }

            Build(schedule);
            schedule.Modified = DateTime.UtcNow;
        });

        Flash($"Schedule “{Name}” is saved.");
        return RedirectToPage("Index");
    }

    public IActionResult OnPostDelete()
    {
        var schedule = Find();
        if (schedule is null)
        {
            return NotFound();
        }

        config.Update(c => c.Schedules.RemoveAll(s => s.Id == schedule.Id));
        Flash($"Schedule “{schedule.Name}” is deleted. Its backups are still at the target.");
        return RedirectToPage("Index");
    }

    private BackupSchedule? Find() => config.Value.Schedules.FirstOrDefault(s => s.Id == Id);

    /// <summary>Copies the form into a schedule. The fields of the other frequencies are left as they were (a hidden field posts nothing).</summary>
    private BackupSchedule Build(BackupSchedule s)
    {
        s.Name = Name;
        s.Enabled = Enabled;
        s.TargetId = TargetId;
        s.Frequency = Frequency;
        switch (Frequency)
        {
            case BackupFrequency.Hourly:
                s.Minute = Minute;
                s.EveryHours = EveryHours;
                break;
            case BackupFrequency.Daily:
            case BackupFrequency.Weekly:
            case BackupFrequency.Monthly:
                s.Time = ScheduleCalculator.TryParseTime(Time, out var h, out var m) ? $"{h:00}:{m:00}" : Time;
                if (Frequency == BackupFrequency.Weekly)
                {
                    s.DayOfWeek = DayOfWeek;
                }
                else if (Frequency == BackupFrequency.Monthly)
                {
                    s.DayOfMonth = DayOfMonth;
                }

                break;
            case BackupFrequency.Cron:
                s.Cron = Cron.Trim();
                break;
        }

        s.Retention = new BackupRetention { KeepLast = KeepLast, KeepDaily = KeepDaily, KeepWeekly = KeepWeekly, KeepMonthly = KeepMonthly };
        s.IncludeKeys = IncludeKeys;
        return s;
    }

    private void Prepare()
    {
        ViewData["Title"] = IsNew ? "New schedule" : "Edit schedule";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("Schedules", "/Admin/Backups/Schedules"), (IsNew ? "New schedule" : Name, null));
    }
}

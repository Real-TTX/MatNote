using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Admin.Backups.Schedules;

/// <summary>The schedules: when a backup is made, where it goes, when it runs next and how the last run went. "Run now" makes a backup at once.</summary>
public sealed class IndexModel(BackupStatusService status, BackupService backups) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<ScheduleState> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var q = ListQuery.Text(Request, "q");
        var state = ListQuery.Text(Request, "state");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var descending = ListQuery.Descending(Request);

        var all = await status.GetAsync();
        var rows = all.AsEnumerable();
        if (q is not null)
        {
            rows = rows.Where(s => s.Schedule.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.TargetName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || ScheduleCalculator.Describe(s.Schedule).Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (state is "enabled" or "disabled")
        {
            rows = rows.Where(s => s.Schedule.Enabled == (state == "enabled"));
        }
        else
        {
            state = null;
        }

        var far = DateTime.MaxValue;
        var matching = (sort, descending) switch
        {
            ("next", false) => rows.OrderBy(s => s.NextRunUtc ?? far).ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ("next", true) => rows.OrderByDescending(s => s.NextRunUtc ?? DateTime.MinValue).ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ("last", false) => rows.OrderBy(s => s.LastRun?.StartDate ?? DateTime.MinValue).ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ("last", true) => rows.OrderByDescending(s => s.LastRun?.StartDate ?? DateTime.MinValue).ThenBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            (_, true) => rows.OrderByDescending(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rows.OrderBy(s => s.Schedule.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching.Count / (double)PageSize)));
        Items = ListQuery.Slice(matching, page, PageSize).ToList();
        Toolbar = new ToolbarModel
        {
            Id = "schedules",
            Action = "/Admin/Backups/Schedules",
            Label = "Schedules",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search schedules",
            Noun = "schedules",
            Total = all.Count,
            Matching = matching.Count,
            Sort = sort,
            Descending = descending,
            DefaultSort = "name",
            SortOptions = [new("name", "Name"), new("next", "Next run"), new("last", "Last run")],
            Filters = [new FilterField("state", "State", [new("enabled", "Enabled"), new("disabled", "Disabled")], state)],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching.Count, Url = Toolbar.UrlWithPage };

        ViewData["Title"] = "Backup schedules";
        ViewData["PageSub"] = "When a backup is made, where it goes and how long the backups are kept.";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("Schedules", null));
    }

    public IActionResult OnPostRun(string id)
    {
        if (backups.TryStartInBackground(id, "manual", out var message))
        {
            Flash(message);
        }
        else
        {
            FlashError(message);
        }

        return RedirectToPage();
    }
}

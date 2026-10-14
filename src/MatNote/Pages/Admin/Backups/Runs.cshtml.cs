using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Admin.Backups;

/// <summary>The history of the runs (<c>BackupRun</c>): every run of a backup, scheduled or manual, with its result and its message.</summary>
public sealed class RunsModel(AppDbContext db) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<BackupRun> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var q = ListQuery.Text(Request, "q");
        var statusText = ListQuery.Text(Request, "status");
        var trigger = ListQuery.Text(Request, "trigger");
        var sort = ListQuery.Text(Request, "sort") ?? "start";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var runs = db.BackupRuns.AsNoTracking();
        var total = await runs.CountAsync();
        if (q is not null)
        {
            var like = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            runs = runs.Where(r => EF.Functions.ILike(r.ScheduleName, like) || EF.Functions.ILike(r.TargetName, like) || (r.Message != null && EF.Functions.ILike(r.Message, like)) || (r.FileName != null && EF.Functions.ILike(r.FileName, like)));
        }

        if (Enum.TryParse<BackupStatus>(statusText, out var status) && Enum.IsDefined(status))
        {
            runs = runs.Where(r => r.Status == status);
        }
        else
        {
            statusText = null;
        }

        if (trigger is "manual" or "scheduled")
        {
            runs = runs.Where(r => r.Trigger == trigger);
        }
        else
        {
            trigger = null;
        }

        runs = (sort, descending) switch
        {
            ("size", false) => runs.OrderBy(r => r.SizeBytes).ThenBy(r => r.StartDate),
            ("size", true) => runs.OrderByDescending(r => r.SizeBytes).ThenByDescending(r => r.StartDate),
            ("schedule", false) => runs.OrderBy(r => r.ScheduleName).ThenByDescending(r => r.StartDate),
            ("schedule", true) => runs.OrderByDescending(r => r.ScheduleName).ThenByDescending(r => r.StartDate),
            (_, false) => runs.OrderBy(r => r.StartDate),
            _ => runs.OrderByDescending(r => r.StartDate),
        };

        var matching = await runs.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = await runs.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

        Toolbar = new ToolbarModel
        {
            Id = "runs",
            Action = "/Admin/Backups/Runs",
            Label = "Runs",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search the history",
            Noun = "runs",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "start",
            DefaultDescending = true,
            SortOptions = [new("start", "Started"), new("schedule", "Schedule"), new("size", "Size")],
            Filters =
            [
                new FilterField("status", "Result", [new("Succeeded", "Succeeded"), new("Failed", "Failed"), new("Running", "Running")], statusText),
                new FilterField("trigger", "Trigger", [new("scheduled", "Scheduled"), new("manual", "Manual")], trigger),
            ],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };

        ViewData["Title"] = "History of the backups";
        ViewData["PageSub"] = "Every run of a backup, scheduled or manual, newest first.";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("History", null));
    }
}

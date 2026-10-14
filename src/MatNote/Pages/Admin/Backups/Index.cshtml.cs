using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Admin.Backups;

/// <summary>A backup file at a target, with what the file name says about it.</summary>
public sealed record BackupRow(string TargetId, string TargetName, string File, string ScheduleId, string ScheduleName, DateTime Created, long Size);

/// <summary>
/// The backups: the files found at the targets (newest first), what needs attention, "run a backup now" and the last runs. Download, restore (its own page) and delete work on a file.
/// Everything that comes from the page (target id, file name) is looked up in the configuration and checked against the file name pattern.
/// </summary>
public sealed class IndexModel(AppDbContext db, ConfigStore<BackupConfig> config, BackupTargetFactory targets, BackupService backups, BackupStatusService status, Clock clock, ILogger<IndexModel> logger) : AppPageModel
{
    private const int PageSize = 20;
    private static readonly TimeSpan ListLimit = TimeSpan.FromSeconds(60);

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<BackupRow> Items { get; private set; } = [];

    public List<BackupWarning> Warnings { get; private set; } = [];

    public List<BackupRun> RecentRuns { get; private set; } = [];

    public List<Choice> ScheduleChoices { get; private set; } = [];

    public bool HasTargets => config.Value.Targets.Count > 0;

    public bool HasSchedules => config.Value.Schedules.Count > 0;

    /// <summary>The name of the schedule that is running at the moment, if any.</summary>
    public string? Running => backups.Current;

    public async Task OnGetAsync()
    {
        var current = config.Value;
        var q = ListQuery.Text(Request, "q");
        var scheduleFilter = ListQuery.Text(Request, "schedule");
        var targetFilter = ListQuery.Text(Request, "target");
        var sort = ListQuery.Text(Request, "sort") ?? "created";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var (all, errors) = await ListAllAsync();
        var rows = all.AsEnumerable();
        if (scheduleFilter is not null)
        {
            rows = rows.Where(r => r.ScheduleId == scheduleFilter);
        }

        if (targetFilter is not null)
        {
            rows = rows.Where(r => r.TargetId == targetFilter);
        }

        if (q is not null)
        {
            rows = rows.Where(r => r.File.Contains(q, StringComparison.OrdinalIgnoreCase) || r.ScheduleName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.TargetName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var matching = rows.ToList();
        matching = (sort, descending) switch
        {
            ("size", false) => [.. matching.OrderBy(r => r.Size).ThenBy(r => r.Created)],
            ("size", true) => [.. matching.OrderByDescending(r => r.Size).ThenByDescending(r => r.Created)],
            ("schedule", false) => [.. matching.OrderBy(r => r.ScheduleName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Created)],
            ("schedule", true) => [.. matching.OrderByDescending(r => r.ScheduleName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Created)],
            ("target", false) => [.. matching.OrderBy(r => r.TargetName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Created)],
            ("target", true) => [.. matching.OrderByDescending(r => r.TargetName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Created)],
            (_, false) => [.. matching.OrderBy(r => r.Created)],
            _ => [.. matching.OrderByDescending(r => r.Created)],
        };

        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching.Count / (double)PageSize)));
        Items = ListQuery.Slice(matching, page, PageSize).ToList();

        var scheduleOptions = current.Schedules.Select(s => new FilterOption(s.Id, s.Name)).ToList();
        foreach (var id in all.Select(r => r.ScheduleId).Distinct().Where(id => scheduleOptions.All(o => o.Value != id)))
        {
            scheduleOptions.Add(new FilterOption(id, BackupUi.ScheduleName(id, current.Schedules)));
        }

        Toolbar = new ToolbarModel
        {
            Id = "backups",
            Action = "/Admin/Backups",
            Label = "Backups",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search backups",
            Noun = "backups",
            Total = all.Count,
            Matching = matching.Count,
            Sort = sort,
            Descending = descending,
            DefaultSort = "created",
            DefaultDescending = true,
            SortOptions = [new("created", "Created"), new("size", "Size"), new("schedule", "Schedule"), new("target", "Target")],
            Filters =
            [
                new FilterField("schedule", "Schedule", scheduleOptions, scheduleFilter),
                new FilterField("target", "Target", current.Targets.Select(t => new FilterOption(t.Id, t.Name)).ToList(), targetFilter),
            ],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching.Count, Url = Toolbar.UrlWithPage };

        ScheduleChoices = current.Schedules.Select(s => new Choice(s.Id, s.Name)).ToList();
        RecentRuns = await db.BackupRuns.AsNoTracking().OrderByDescending(r => r.StartDate).Take(8).ToListAsync();
        Warnings = await BuildWarningsAsync(all.Count > 0, errors);

        ViewData["Title"] = "Backups";
        ViewData["PageSub"] = "The backups at your targets, what was made lately, and a button to make one now.";
        Crumbs(("Administration", null), ("Backups", null));
    }

    /// <summary>Makes a backup now, in the background; the history shows when it is done.</summary>
    public IActionResult OnPostRun(string scheduleId)
    {
        if (backups.TryStartInBackground(scheduleId, "manual", out var message))
        {
            Flash(message);
        }
        else
        {
            FlashError(message);
        }

        return RedirectToPage();
    }

    /// <summary>The file goes through the temp folder (an SMB share is read to the end first) and is deleted when the download is over.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(string target, string file)
    {
        var found = FindTarget(target);
        if (found is null || !BackupFileName.IsValid(file))
        {
            return NotFound();
        }

        var temp = Path.Combine(HttpContext.RequestServices.GetRequiredService<AppPaths>().Temp, "backup-download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            limit.CancelAfter(TimeSpan.FromHours(2));
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await targets.Create(found).DownloadAsync(file, output, limit.Token);
            }

            var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            return File(stream, "application/zip", file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            FlashError(ex.Message);
            return RedirectToPage();
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string target, string file)
    {
        var found = FindTarget(target);
        if (found is null || !BackupFileName.IsValid(file))
        {
            return NotFound();
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            limit.CancelAfter(ListLimit);
            await targets.Create(found).DeleteAsync(file, limit.Token);
            Flash($"The backup {file} is deleted.");
        }
        catch (BackupTargetException ex)
        {
            FlashError(ex.Message);
        }

        return RedirectToPage();
    }

    private BackupTarget? FindTarget(string id) => config.Value.Targets.FirstOrDefault(t => t.Id == id);

    /// <summary>The files of every target (at the same time). A target that cannot be reached does not break the page: it is reported.</summary>
    private async Task<(List<BackupRow> Rows, List<string> Errors)> ListAllAsync()
    {
        var current = config.Value;
        var errors = new List<string>();
        var rows = new List<BackupRow>();
        var results = await Task.WhenAll(current.Targets.Select(async target =>
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                limit.CancelAfter(ListLimit);
                return (target, files: await targets.Create(target).ListAsync(limit.Token), error: (string?)null);
            }
            catch (Exception ex) when (ex is BackupTargetException or OperationCanceledException or InvalidOperationException)
            {
                logger.LogWarning(ex, "The target {Target} could not be listed.", target.Name);
                return (target, files: (IReadOnlyList<BackupFile>)[], error: ex is OperationCanceledException ? "no answer in time" : ex.Message);
            }
        }));

        foreach (var (target, files, error) in results)
        {
            if (error is not null)
            {
                errors.Add($"{target.Name}: {error}");
                continue;
            }

            foreach (var f in files)
            {
                if (BackupFileName.TryParse(f.Name, out var scheduleId, out var created))
                {
                    rows.Add(new BackupRow(target.Id, target.Name, f.Name, scheduleId, BackupUi.ScheduleName(scheduleId, current.Schedules), created, f.Size));
                }
            }
        }

        return (rows, errors);
    }

    private async Task<List<BackupWarning>> BuildWarningsAsync(bool anyFiles, List<string> targetErrors)
    {
        var warnings = new List<BackupWarning>();
        var current = config.Value;
        foreach (var error in targetErrors)
        {
            warnings.Add(new BackupWarning("A target cannot be reached", error, "/Admin/Backups/Targets", "Check the targets"));
        }

        if (current.Targets.Count == 0)
        {
            warnings.Add(new BackupWarning("There is no target yet", "A backup needs a place to go: a folder, or a share on your NAS.", "/Admin/Backups/Targets/Edit", "Add a target"));
        }
        else if (current.Schedules.Count == 0)
        {
            warnings.Add(new BackupWarning("There is no schedule yet", "Without a schedule nothing is backed up by itself.", "/Admin/Backups/Schedules/Edit", "Add a schedule"));
        }

        var states = await status.GetAsync();
        foreach (var state in states.Where(s => s.Schedule.Enabled))
        {
            if (state.LastRun is { Status: BackupStatus.Failed } failed)
            {
                warnings.Add(new BackupWarning($"The last backup of “{state.Schedule.Name}” failed", $"{clock.Format(failed.StartDate)}: {failed.Message}", "/Admin/Backups/Runs?status=Failed", "Show the history"));
            }
        }

        if (!anyFiles && !await db.BackupRuns.AnyAsync(r => r.Status == BackupStatus.Succeeded))
        {
            warnings.Add(new BackupWarning("No successful backup exists", "Nothing has been backed up yet. Run a backup now to be sure it works."));
        }

        return warnings;
    }

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cleanup of the temp folder at the next start takes it
        }
    }
}

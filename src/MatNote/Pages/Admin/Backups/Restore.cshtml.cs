using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Admin.Backups;

/// <summary>
/// Restore a backup. The page is a warning and two choices; the restore itself runs in the request (a restore is what the administrator waits for) and the answer is a page that says
/// what happened, with the log of <c>pg_restore</c>. The target and the file name from the page are looked up in the configuration and checked against the file name pattern.
/// </summary>
public sealed class RestoreModel(ConfigStore<BackupConfig> config, RestoreService restore) : AppPageModel
{
    [BindProperty(Name = "target", SupportsGet = true)] public string TargetId { get; set; } = string.Empty;

    [BindProperty(Name = "file", SupportsGet = true)] public string FileName { get; set; } = string.Empty;

    [BindProperty] public bool RestoreKeys { get; set; }

    [BindProperty] public bool SafetyBackup { get; set; } = true;

    public RestoreResult? Result { get; private set; }

    public string TargetName { get; private set; } = string.Empty;

    public string ScheduleName { get; private set; } = string.Empty;

    public DateTime Created { get; private set; }

    public IActionResult OnGet() => Fill() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Fill())
        {
            return NotFound();
        }

        // the restore must not stop half way because the browser went away: it has its own limit
        Result = await restore.RestoreAsync(new RestoreOptions(TargetId, FileName, RestoreKeys, SafetyBackup), CancellationToken.None);
        ViewData["Title"] = Result.Succeeded ? "Restore done" : "Restore failed";
        return Page();
    }

    private bool Fill()
    {
        var target = config.Value.Targets.FirstOrDefault(t => t.Id == TargetId);
        if (target is null || !BackupFileName.TryParse(FileName, out var scheduleId, out var created))
        {
            return false;
        }

        TargetName = target.Name;
        ScheduleName = BackupUi.ScheduleName(scheduleId, config.Value.Schedules);
        Created = created;
        ViewData["Title"] = "Restore a backup";
        ViewData["PageSub"] = "Replaces the data of the app with the state of a backup.";
        ViewData["Width"] = "narrow";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("Restore", null));
        return true;
    }
}

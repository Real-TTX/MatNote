using System.Diagnostics;
using System.Runtime.InteropServices;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Admin.System;

/// <summary>The state of the app and its settings (kept in <c>config/app.json</c>): registration, sessions, the size of uploads, the trash, the public address, the time zone.</summary>
public sealed class IndexModel(AppDbContext db, ConfigStore<AppConfig> config, AppPaths paths, MaintenanceService maintenance) : AppPageModel
{
    [BindProperty] public bool AllowRegistration { get; set; }

    [BindProperty] public int SessionDays { get; set; } = 14;

    [BindProperty] public int MaxUploadMb { get; set; } = 50;

    [BindProperty] public int TrashDays { get; set; } = 30;

    [BindProperty] public string PublicBaseUrl { get; set; } = string.Empty;

    [BindProperty] public string TimeZone { get; set; } = string.Empty;

    public string Version => AppInfo.Version;

    public string DataDir => paths.DataDir;

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string System => RuntimeInformation.OSDescription;

    public TimeSpan Uptime => DateTime.Now - Process.GetCurrentProcess().StartTime;

    public string ZoneInUse => TimeZoneInfo.Local.Id;

    public string? DatabaseVersion { get; private set; }

    public string? DatabaseSize { get; private set; }

    public bool DatabaseOk { get; private set; }

    public int Users { get; private set; }

    public int NoteCount { get; private set; }

    public long FileBytes { get; private set; }

    public BackupRun? LastBackup { get; private set; }

    public async Task OnGetAsync()
    {
        var c = config.Value;
        AllowRegistration = c.AllowRegistration;
        SessionDays = c.SessionDays;
        MaxUploadMb = c.MaxUploadMb;
        TrashDays = c.TrashDays;
        PublicBaseUrl = c.PublicBaseUrl;
        TimeZone = c.TimeZone;
        await LoadStateAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (SessionDays is < 1 or > 365)
        {
            ModelState.AddModelError(nameof(SessionDays), "From 1 to 365 days.");
        }

        if (MaxUploadMb is < 1 or > 500)
        {
            ModelState.AddModelError(nameof(MaxUploadMb), "From 1 to 500 MB.");
        }

        if (TrashDays is < 1 or > 3650)
        {
            ModelState.AddModelError(nameof(TrashDays), "From 1 to 3650 days.");
        }

        PublicBaseUrl = PublicBaseUrl.Trim().TrimEnd('/');
        if (PublicBaseUrl.Length > 0 && (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            ModelState.AddModelError(nameof(PublicBaseUrl), "Please enter an address like https://notes.example.com.");
        }

        TimeZone = TimeZone.Trim();
        if (TimeZone.Length > 0)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                ModelState.AddModelError(nameof(TimeZone), "This time zone is not known here. Use a name like Europe/Berlin.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadStateAsync();
            return Page();
        }

        config.Update(c =>
        {
            c.AllowRegistration = AllowRegistration;
            c.SessionDays = SessionDays;
            c.MaxUploadMb = MaxUploadMb;
            c.TrashDays = TrashDays;
            c.PublicBaseUrl = PublicBaseUrl;
            c.TimeZone = TimeZone;
        });
        Flash("The settings are saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostHousekeepingAsync()
    {
        await maintenance.RunOnceAsync(HttpContext.RequestAborted);
        Flash("The housekeeping ran: old sessions and old notes of the trash were removed.");
        return RedirectToPage();
    }

    private async Task LoadStateAsync()
    {
        try
        {
            DatabaseOk = await db.Database.CanConnectAsync();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version(), pg_size_pretty(pg_database_size(current_database()))";
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    DatabaseVersion = reader.GetString(0).Split(',')[0];
                    DatabaseSize = reader.GetString(1);
                }
            }

            await conn.CloseAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DatabaseOk = false;
        }

        Users = await db.Users.CountAsync();
        NoteCount = await db.Notes.CountAsync();
        FileBytes = await db.Notes.SumAsync(n => n.FileSize);
        LastBackup = await db.BackupRuns.AsNoTracking().OrderByDescending(r => r.StartDate).FirstOrDefaultAsync();
        Crumbs(("Administration", null), ("System", null));
    }
}

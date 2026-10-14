using System.Globalization;
using System.Net;
using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Html;

namespace MatNote.Pages.Admin.Backups;

/// <summary>A group of radio buttons for the edit pages (up to five choices are radios, FORM-12); other fields can depend on it with <c>show-if="Name"</c>.</summary>
public sealed record RadioGroup(string Name, string Label, IReadOnlyList<Choice> Choices, string Value, string? Hint = null);

/// <summary>A message at the top of the Backups page: something an administrator has to look at.</summary>
public sealed record BackupWarning(string Title, string Text, string? Link = null, string? LinkText = null);

/// <summary>Small helpers for the markup of the backup pages.</summary>
public static class BackupUi
{
    /// <summary>The status of a run as a pill: running, done or failed.</summary>
    public static IHtmlContent Pill(BackupStatus status) => status switch
    {
        BackupStatus.Succeeded => new HtmlString("<span class=\"mat-pill mat-pill--ok\">Succeeded</span>"),
        BackupStatus.Failed => new HtmlString("<span class=\"mat-pill mat-pill--error\">Failed</span>"),
        _ => new HtmlString("<span class=\"mat-pill mat-pill--busy\">Running</span>"),
    };

    public static string Duration(BackupRun run)
    {
        if (run.EndDate is not { } end)
        {
            return string.Empty;
        }

        var span = end - run.StartDate;
        return span.TotalSeconds < 1 ? "under 1 s" : span.TotalMinutes < 1 ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0} s") : string.Create(CultureInfo.InvariantCulture, $"{span.TotalMinutes:0.#} min");
    }

    public static string Trigger(string trigger) => trigger == "scheduled" ? "Scheduled" : "Manual";

    public static string TypeName(BackupTargetType type) => type == BackupTargetType.Smb ? "SMB share (NAS)" : "Folder";

    public static string TypeIcon(BackupTargetType type) => type == BackupTargetType.Smb ? "network" : "folder";

    /// <summary>The name of a schedule as the history and the list of files show it: the schedule of the file may be deleted, or be the safety backup of a restore.</summary>
    public static string ScheduleName(string id, IEnumerable<BackupSchedule> schedules)
        => id == BackupFileName.PreRestoreId ? "Before a restore" : schedules.FirstOrDefault(s => s.Id == id)?.Name ?? "(deleted schedule)";

    public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}

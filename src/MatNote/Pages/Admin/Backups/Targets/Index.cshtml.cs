using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Admin.Backups.Targets;

public sealed record TargetRow(string Id, string Name, BackupTargetType Type, string Location, int Schedules);

/// <summary>The targets: where the backups go. Toolbar above, table, list actions below; "Test" asks the target and says what it found.</summary>
public sealed class IndexModel(ConfigStore<BackupConfig> config, BackupTargetFactory targets) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<TargetRow> Items { get; private set; } = [];

    public void OnGet()
    {
        var current = config.Value;
        var q = ListQuery.Text(Request, "q");
        var type = ListQuery.Text(Request, "type");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var descending = ListQuery.Descending(Request);

        var all = current.Targets.Select(t => new TargetRow(t.Id, t.Name, t.Type, t.Location, current.Schedules.Count(s => s.TargetId == t.Id))).ToList();
        var rows = all.AsEnumerable();
        if (q is not null)
        {
            rows = rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Location.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (Enum.TryParse<BackupTargetType>(type, out var kind) && Enum.IsDefined(kind))
        {
            rows = rows.Where(r => r.Type == kind);
        }
        else
        {
            type = null;
        }

        var matching = (sort, descending) switch
        {
            ("type", false) => rows.OrderBy(r => r.Type).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ("type", true) => rows.OrderByDescending(r => r.Type).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            (_, true) => rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching.Count / (double)PageSize)));
        Items = ListQuery.Slice(matching, page, PageSize).ToList();
        Toolbar = new ToolbarModel
        {
            Id = "targets",
            Action = "/Admin/Backups/Targets",
            Label = "Targets",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search targets",
            Noun = "targets",
            Total = all.Count,
            Matching = matching.Count,
            Sort = sort,
            Descending = descending,
            DefaultSort = "name",
            SortOptions = [new("name", "Name"), new("type", "Type")],
            Filters = [new FilterField("type", "Type", [new(nameof(BackupTargetType.Folder), "Folder"), new(nameof(BackupTargetType.Smb), "SMB share (NAS)")], type)],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching.Count, Url = Toolbar.UrlWithPage };

        ViewData["Title"] = "Backup targets";
        ViewData["PageSub"] = "Where the backups are kept: a folder (a volume or a mounted NAS) or a share on your NAS, reached over SMB.";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("Targets", null));
    }

    public async Task<IActionResult> OnPostTestAsync(string id)
    {
        var target = config.Value.Targets.FirstOrDefault(t => t.Id == id);
        if (target is null)
        {
            return NotFound();
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            limit.CancelAfter(TimeSpan.FromSeconds(90));
            Flash($"“{target.Name}”: {await targets.Create(target).TestConnectionAsync(limit.Token)}");
        }
        catch (BackupTargetException ex)
        {
            FlashError($"“{target.Name}”: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            FlashError($"“{target.Name}”: no answer in time.");
        }

        return RedirectToPage();
    }
}

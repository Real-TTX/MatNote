using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Trash;

public sealed record TrashRow(long Id, string Title, NoteType Type, DateTime Deleted);

/// <summary>The notes that were deleted: they stay here for a while (the setting of the administrator) and can be restored. Only the owner sees them.</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me, NoteService notes, ConfigStore<AppConfig> config) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<TrashRow> Items { get; private set; } = [];

    public int Days => Math.Max(1, config.Value.TrashDays);

    public int Total { get; private set; }

    /// <summary>How many days are left before a note is removed for good.</summary>
    public int DaysLeft(TrashRow row) => Math.Max(0, Days - (int)(DateTime.UtcNow - row.Deleted).TotalDays);

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        var q = ListQuery.Text(Request, "q");
        var sort = ListQuery.Text(Request, "sort") ?? "deleted";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var all = db.Notes.IgnoreQueryFilters().AsNoTracking().Where(n => n.OwnerId == id && n.UpdateState == SoftEntity.Deleted);
        Total = await all.CountAsync();
        var query = all;
        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(n => EF.Functions.ILike(n.Title, pattern));
        }

        var rows = query.Select(n => new { n.Id, n.Title, n.Type, Deleted = n.UpdateDate });
        rows = (sort, descending) switch
        {
            ("title", false) => rows.OrderBy(r => r.Title),
            ("title", true) => rows.OrderByDescending(r => r.Title),
            (_, false) => rows.OrderBy(r => r.Deleted),
            _ => rows.OrderByDescending(r => r.Deleted),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new TrashRow(r.Id, r.Title, r.Type, r.Deleted)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "trash",
            Action = "/Trash",
            Label = "Trash",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search the trash",
            Noun = "notes",
            Total = Total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "deleted",
            DefaultDescending = true,
            SortOptions = [new("deleted", "Deleted on"), new("title", "Title")],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        Crumbs(("Trash", null));
    }

    public async Task<IActionResult> OnPostRestoreAsync(long id)
    {
        Flash(await notes.RestoreAsync(id, me.RequiredId) ? "The note is back." : "That note is not in the trash.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPurgeAsync(long id)
    {
        Flash(await notes.PurgeAsync(id, me.RequiredId) ? "The note is deleted for good." : "That note is not in the trash.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEmptyAsync()
    {
        var ids = await db.Notes.IgnoreQueryFilters().Where(n => n.OwnerId == me.RequiredId && n.UpdateState == SoftEntity.Deleted).Select(n => n.Id).ToListAsync();
        foreach (var id in ids)
        {
            await notes.PurgeAsync(id, me.RequiredId);
        }

        Flash(ids.Count == 0 ? "The trash was empty." : $"The trash is empty: {ids.Count} notes were deleted for good.");
        return RedirectToPage();
    }
}

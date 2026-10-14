using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Folders;

public sealed record FolderRow(long Id, string Path, string Name, string? Color, int Depth, int Notes, int Subfolders, bool Shared, DateTime Updated);

/// <summary>The folders of the signed-in person: toolbar, table, pagination and the action to add one. Every folder opens its notes; editing is a page of its own.</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<FolderRow> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        var q = ListQuery.Text(Request, "q");
        var sort = ListQuery.Text(Request, "sort") ?? "path";
        var descending = ListQuery.Descending(Request);

        var folders = await db.Folders.AsNoTracking().Where(f => f.OwnerId == id).Select(f => new { f.Id, f.ParentId, f.Name, f.Color, f.UpdateDate }).ToListAsync();
        var counts = await db.Notes.AsNoTracking().Where(n => n.OwnerId == id && n.FolderId != null).GroupBy(n => n.FolderId!.Value).Select(g => new { Folder = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Folder, g => g.Count);
        var shared = (await db.Shares.AsNoTracking().Where(s => s.OwnerId == id && s.FolderId != null).Select(s => s.FolderId!.Value).Distinct().ToListAsync()).ToHashSet();
        var byId = folders.ToDictionary(f => f.Id);

        string PathOf(long folderId)
        {
            var names = new List<string>();
            long? at = folderId;
            while (at is { } current && byId.TryGetValue(current, out var f) && names.Count < FolderService.MaxDepth + 2)
            {
                names.Insert(0, f.Name);
                at = f.ParentId;
            }

            return string.Join(" / ", names);
        }

        var all = folders.Select(f => new FolderRow(
            f.Id, PathOf(f.Id), f.Name, f.Color, PathOf(f.Id).Count(c => c == '/'),
            counts.GetValueOrDefault(f.Id), folders.Count(x => x.ParentId == f.Id), shared.Contains(f.Id), f.UpdateDate)).ToList();

        IEnumerable<FolderRow> rows = all;
        if (q is not null)
        {
            rows = rows.Where(r => r.Path.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var matching = rows.ToList();
        IEnumerable<FolderRow> ordered = sort switch
        {
            "notes" => matching.OrderBy(r => r.Notes).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase),
            "updated" => matching.OrderBy(r => r.Updated),
            _ => matching.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase),
        };
        var list = ordered.ToList();
        if (descending)
        {
            list.Reverse();
        }

        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(list.Count / (double)PageSize)));
        Items = ListQuery.Slice(list, page, PageSize).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "folders",
            Action = "/Folders",
            Label = "Folders",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search folders",
            Noun = "folders",
            Total = all.Count,
            Matching = matching.Count,
            Sort = sort,
            Descending = descending,
            DefaultSort = "path",
            SortOptions = [new("path", "Name"), new("notes", "Number of notes"), new("updated", "Last changed")],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching.Count, Url = Toolbar.UrlWithPage };
        Crumbs(("Folders", null));
    }
}

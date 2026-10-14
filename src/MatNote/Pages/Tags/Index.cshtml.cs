using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Tags;

public sealed record TagRow(long Id, string Name, string? Color, int Notes, DateTime Updated);

/// <summary>The tags of the signed-in person: toolbar above (search, sort), the table, the pagination under it and the action to add one below.</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<TagRow> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        var q = ListQuery.Text(Request, "q");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var descending = ListQuery.Descending(Request);

        var tags = db.Tags.AsNoTracking().Where(t => t.OwnerId == id);
        var total = await tags.CountAsync();
        if (q is not null)
        {
            tags = tags.Where(t => EF.Functions.ILike(t.Name, "%" + q.Replace("%", "\\%").Replace("_", "\\_") + "%"));
        }

        var rows = tags.Select(t => new { t.Id, t.Name, t.Color, Notes = db.NoteTags.Count(nt => nt.TagId == t.Id && nt.Note!.OwnerId == id), Updated = t.UpdateDate });
        rows = (sort, descending) switch
        {
            ("notes", false) => rows.OrderBy(r => r.Notes).ThenBy(r => r.Name),
            ("notes", true) => rows.OrderByDescending(r => r.Notes).ThenBy(r => r.Name),
            ("updated", false) => rows.OrderBy(r => r.Updated),
            ("updated", true) => rows.OrderByDescending(r => r.Updated),
            (_, true) => rows.OrderByDescending(r => r.Name),
            _ => rows.OrderBy(r => r.Name),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new TagRow(r.Id, r.Name, r.Color, r.Notes, r.Updated)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "tags",
            Action = "/Tags",
            Label = "Tags",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search tags",
            Noun = "tags",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "name",
            SortOptions = [new("name", "Name"), new("notes", "Number of notes"), new("updated", "Last changed")],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        Crumbs(("Tags", null));
    }
}

using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Shares;

public sealed record MyShareRow(long Id, ShareKind Kind, string Name, string TargetKind, NoteType? Type, string? WithUser, string? WithDisplayName, SharePermission Permission, bool HasPassword, DateTime Created, DateTime? Expires, int Views, Guid Token);

/// <summary>What the signed-in person shared: with users and by link. Editing (and stopping) a share is a page of its own.</summary>
public sealed class MineModel(AppDbContext db, CurrentUser me, ShareService shares) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<MyShareRow> Items { get; private set; } = [];

    public string LinkUrl(MyShareRow row) => shares.LinkUrl(Request, new Share { Token = row.Token });

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        var q = ListQuery.Text(Request, "q");
        var kind = ListQuery.Text(Request, "kind");
        var what = ListQuery.Text(Request, "what");
        var sort = ListQuery.Text(Request, "sort") ?? "created";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var all = db.Shares.AsNoTracking().Where(s => s.OwnerId == id && (s.NoteId != null || s.FolderId != null));
        var total = await all.CountAsync();
        var query = all;
        if (kind is "user" or "link")
        {
            var k = kind == "user" ? ShareKind.User : ShareKind.Link;
            query = query.Where(s => s.Kind == k);
        }
        else
        {
            kind = null;
        }

        if (what == "note")
        {
            query = query.Where(s => s.NoteId != null);
        }
        else if (what == "folder")
        {
            query = query.Where(s => s.FolderId != null);
        }
        else
        {
            what = null;
        }

        var rows = query.Select(s => new
        {
            s.Id, s.Kind,
            Name = s.NoteId != null ? s.Note!.Title : s.Folder!.Name,
            TargetKind = s.NoteId != null ? "Note" : "Folder",
            Type = s.NoteId != null ? (NoteType?)s.Note!.Type : null,
            WithUser = s.SharedWithUser != null ? s.SharedWithUser.Username : null,
            WithDisplayName = s.SharedWithUser != null ? s.SharedWithUser.DisplayName : null,
            s.Permission, HasPassword = s.PasswordHash != null, Created = s.CreateDate, Expires = s.ExpiresDate, Views = s.ViewCount, s.Token,
        });
        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(r => EF.Functions.ILike(r.Name, pattern) || (r.WithUser != null && EF.Functions.ILike(r.WithUser, pattern)));
        }

        rows = (sort, descending) switch
        {
            ("name", false) => rows.OrderBy(r => r.Name),
            ("name", true) => rows.OrderByDescending(r => r.Name),
            ("views", false) => rows.OrderBy(r => r.Views),
            ("views", true) => rows.OrderByDescending(r => r.Views),
            (_, false) => rows.OrderBy(r => r.Created),
            _ => rows.OrderByDescending(r => r.Created),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new MyShareRow(r.Id, r.Kind, r.Name, r.TargetKind, r.Type, r.WithUser, r.WithDisplayName, r.Permission, r.HasPassword, r.Created, r.Expires, r.Views, r.Token)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "mine",
            Action = "/Shares/Mine",
            Label = "Shared by me",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search by name or user name",
            Noun = "shares",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "created",
            DefaultDescending = true,
            SortOptions = [new("created", "Shared on"), new("name", "Name"), new("views", "Views")],
            Filters =
            [
                new("kind", "With", [new("user", "A user"), new("link", "Anyone with the link")], kind),
                new("what", "What", [new("note", "Notes"), new("folder", "Folders")], what),
            ],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        ViewData["Title"] = "Shares";
        ViewData["ActiveNav"] = "shares";
        Crumbs(("Shares", "/Shares"), ("Shared by me", null));
    }
}

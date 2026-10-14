using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Shares;

public sealed record SharedWithMeRow(long ShareId, long? NoteId, long? FolderId, string Name, string Kind, NoteType? Type, string OwnerName, SharePermission Permission, DateTime Created, DateTime? Expires);

/// <summary>What other people shared with the signed-in person (the notes and folders), with who shared it and what they may do. A person sees only the name of the one who shared with them.</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<SharedWithMeRow> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        var now = DateTime.UtcNow;
        var q = ListQuery.Text(Request, "q");
        var kind = ListQuery.Text(Request, "kind");
        var sort = ListQuery.Text(Request, "sort") ?? "created";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var shares = db.Shares.AsNoTracking().Where(s => s.Kind == ShareKind.User && s.SharedWithUserId == id && (s.ExpiresDate == null || s.ExpiresDate > now)
                                                         && (s.NoteId != null || s.FolderId != null));
        var total = await shares.CountAsync();
        if (kind == "note")
        {
            shares = shares.Where(s => s.NoteId != null);
        }
        else if (kind == "folder")
        {
            shares = shares.Where(s => s.FolderId != null);
        }
        else
        {
            kind = null;
        }

        var rows = shares.Select(s => new
        {
            ShareId = s.Id, s.NoteId, s.FolderId,
            Name = s.NoteId != null ? s.Note!.Title : s.Folder!.Name,
            Kind = s.NoteId != null ? "Note" : "Folder",
            Type = s.NoteId != null ? (NoteType?)s.Note!.Type : null,
            OwnerName = db.Users.Where(u => u.Id == s.OwnerId).Select(u => u.DisplayName).FirstOrDefault() ?? string.Empty,
            s.Permission, Created = s.CreateDate, Expires = s.ExpiresDate,
        });
        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(r => EF.Functions.ILike(r.Name, pattern) || EF.Functions.ILike(r.OwnerName, pattern));
        }

        rows = (sort, descending) switch
        {
            ("name", false) => rows.OrderBy(r => r.Name),
            ("name", true) => rows.OrderByDescending(r => r.Name),
            ("owner", false) => rows.OrderBy(r => r.OwnerName).ThenBy(r => r.Name),
            ("owner", true) => rows.OrderByDescending(r => r.OwnerName).ThenBy(r => r.Name),
            (_, false) => rows.OrderBy(r => r.Created),
            _ => rows.OrderByDescending(r => r.Created),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new SharedWithMeRow(r.ShareId, r.NoteId, r.FolderId, r.Name, r.Kind, r.Type, r.OwnerName, r.Permission, r.Created, r.Expires)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "with-me",
            Action = "/Shares",
            Label = "Shared with me",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search by name or by who shared it",
            Noun = "shares",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "created",
            DefaultDescending = true,
            SortOptions = [new("created", "Shared on"), new("name", "Name"), new("owner", "Shared by")],
            Filters = [new("kind", "What", [new("note", "Notes"), new("folder", "Folders")], kind)],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        ViewData["ActiveNav"] = "shares";
        Crumbs(("Shares", null));
    }
}

using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Notes;

public sealed record TagView(string Name, string? Color, long Id = 0);

public sealed record NoteRow(long Id, string Title, NoteType Type, string? FolderName, bool Favorite, DateTime Updated, bool Own, string OwnerName, long FileSize)
{
    public List<TagView> Tags { get; set; } = [];

    public Access Access { get; set; }
}

public sealed record SubfolderView(long Id, string Name, string? Color);

/// <summary>
/// The notes the signed-in person may see (their own and what was shared with them): all of them, the favorites, one folder or one tag.
/// A toolbar above (search, filter by type and tag, sort), the table, the pagination under it and the actions below.
/// </summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me, AccessService access, FolderService folders) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<NoteRow> Items { get; private set; } = [];

    public List<SubfolderView> Subfolders { get; private set; } = [];

    public string Heading { get; private set; } = "All notes";

    public string Subtitle { get; private set; } = string.Empty;

    public long? FolderId { get; private set; }

    public bool CanAddHere { get; private set; } = true;

    /// <summary>The folder is the person own (not one that was shared): it can get subfolders.</summary>
    public bool OwnFolder { get; private set; }

    public bool IsFavorites { get; private set; }

    /// <summary>The layout: the list and the picked note side by side (<c>split</c>, on a wide window) or only the list, a note on a page of its own (<c>list</c>). Remembered in a cookie.</summary>
    public bool IsSplit { get; private set; }

    /// <summary>The note shown in the pane (<c>?id=</c>), or <c>null</c>.</summary>
    public long? SelectedId { get; private set; }

    /// <summary>The person works in the edit mode (the cookie <c>mn.mode</c>, set by the page in the pane) and may edit the picked note: it opens in the editor.</summary>
    public bool EditSelected { get; private set; }

    public const string ModeCookie = "mn.mode";

    public string PaneSrc => SelectedId is { } id ? $"/Notes/{(EditSelected ? "Edit" : "View")}?id={id}&embed=1" : $"/Notes/Pane?embed=1{(FolderId is { } f ? "&folder=" + f : string.Empty)}";

    public const string LayoutCookie = "mn.layout";

    public async Task<IActionResult> OnGetAsync()
    {
        var id = me.RequiredId;
        var layout = ListQuery.Text(Request, "layout");
        if (layout is "split" or "list")
        {
            Response.Cookies.Append(LayoutCookie, layout, new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, HttpOnly = true, IsEssential = true });
        }
        else
        {
            layout = Request.Cookies[LayoutCookie];
        }

        IsSplit = layout != "list";
        SelectedId = long.TryParse(ListQuery.Text(Request, "id"), out var selected) && selected > 0 ? selected : null;
        if (SelectedId is { } picked && Request.Cookies[ModeCookie] == "edit")
        {
            var (editable, _) = await access.FindNoteAsync(picked, Access.Edit);
            EditSelected = editable is not null && Ui.IsText(editable.Type);
        }

        var q = ListQuery.Text(Request, "q");
        var type = ListQuery.Text(Request, "type");
        var tagText = ListQuery.Text(Request, "tag");
        var scope = ListQuery.Text(Request, "scope");
        var folderText = ListQuery.Text(Request, "folder");
        var sort = ListQuery.Text(Request, "sort") ?? "updated";
        var descending = ListQuery.Descending(Request, byDefault: true);

        var notes = await access.VisibleNotesAsync();
        var crumbs = new List<(string, string?)> { ("Notes", folderText is null && scope is null ? null : "/Notes") };
        IsFavorites = scope == "favorites";

        if (folderText is not null)
        {
            if (!long.TryParse(folderText, out var folderId))
            {
                return NotFound();
            }

            var (folder, folderAccess) = await access.FindFolderAsync(folderId);
            if (folder is null)
            {
                return NotFound();
            }

            FolderId = folder.Id;
            Heading = folder.Name;
            CanAddHere = folderAccess >= Access.Edit;
            OwnFolder = folderAccess == Access.Owner;
            ViewData["ActiveFolder"] = folder.Id;
            notes = notes.Where(n => n.FolderId == folder.Id);
            if (folderAccess == Access.Owner)
            {
                var path = await folders.PathAsync(folder.Id, id);
                foreach (var (pid, name) in path)
                {
                    crumbs.Add((name, pid == folder.Id ? null : $"/Notes?folder={pid}"));
                }

                Subtitle = "Notes in this folder";
            }
            else
            {
                crumbs.Add((folder.Name, null));
                Subtitle = "A folder that was shared with you";
            }

            var scopeFolders = (await access.ScopeAsync()).Folders;
            Subfolders = await db.Folders.AsNoTracking()
                .Where(f => f.ParentId == folder.Id && (f.OwnerId == id || scopeFolders.Keys.Contains(f.Id)))
                .OrderBy(f => f.Name).Select(f => new SubfolderView(f.Id, f.Name, f.Color)).ToListAsync();
        }
        else if (IsFavorites)
        {
            Heading = "Favorites";
            Subtitle = "The notes you starred.";
            notes = notes.Where(n => n.OwnerId == id && n.IsFavorite);
            crumbs.Add(("Favorites", null));
            ViewData["ActiveNav"] = "favorites";
        }
        else
        {
            Subtitle = "Everything you can see: your own notes and what was shared with you.";
            crumbs.Clear();
            crumbs.Add(("Notes", null));
        }

        // the filters of the toolbar
        var tags = await db.Tags.AsNoTracking().Where(t => t.OwnerId == id).OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync();
        if (tagText is not null && long.TryParse(tagText, out var tagId) && tags.Any(t => t.Id == tagId))
        {
            notes = notes.Where(n => n.OwnerId == id && n.Tags.Any(nt => nt.TagId == tagId));
            Heading = FolderId is null && !IsFavorites ? "Tag: " + tags.First(t => t.Id == tagId).Name : Heading;
        }
        else
        {
            tagText = null;
        }

        if (type is not null && Enum.TryParse<NoteType>(type, out var noteType))
        {
            notes = notes.Where(n => n.Type == noteType);
        }
        else
        {
            type = null;
        }

        var total = await notes.CountAsync();
        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            notes = notes.Where(n => EF.Functions.ILike(n.Title, pattern) || EF.Functions.ILike(n.SearchText, pattern));
        }

        var rows = notes.Select(n => new { n.Id, n.Title, n.Type, FolderName = n.Folder != null && n.OwnerId == id ? n.Folder.Name : null, Favorite = n.IsFavorite && n.OwnerId == id, Updated = n.UpdateDate, Own = n.OwnerId == id, OwnerName = n.Owner!.DisplayName, n.FileSize });
        rows = (sort, descending) switch
        {
            ("title", false) => rows.OrderBy(r => r.Title),
            ("title", true) => rows.OrderByDescending(r => r.Title),
            ("type", false) => rows.OrderBy(r => r.Type).ThenBy(r => r.Title),
            ("type", true) => rows.OrderByDescending(r => r.Type).ThenBy(r => r.Title),
            ("updated", false) => rows.OrderBy(r => r.Updated),
            _ => rows.OrderByDescending(r => r.Updated),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new NoteRow(r.Id, r.Title, r.Type, r.FolderName, r.Favorite, r.Updated, r.Own, r.OwnerName, r.FileSize)).ToList();

        var ids = Items.Where(r => r.Own).Select(r => r.Id).ToList();
        if (ids.Count > 0)
        {
            var tagRows = await db.NoteTags.AsNoTracking().Where(nt => ids.Contains(nt.NoteId)).Select(nt => new { nt.NoteId, nt.Tag!.Name, nt.Tag.Color }).ToListAsync();
            foreach (var row in Items)
            {
                row.Tags = tagRows.Where(t => t.NoteId == row.Id).OrderBy(t => t.Name).Select(t => new TagView(t.Name, t.Color)).ToList();
            }
        }

        var keep = new Dictionary<string, string?> { ["folder"] = FolderId?.ToString(), ["scope"] = scope };
        Toolbar = new ToolbarModel
        {
            Id = "notes",
            Action = "/Notes",
            Label = "Notes",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search",
            Noun = "notes",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "updated",
            DefaultDescending = true,
            SortOptions = [new("updated", "Last changed"), new("title", "Title"), new("type", "Type")],
            Filters =
            [
                new("type", "Type", Enum.GetValues<NoteType>().Select(t => new FilterOption(t.ToString(), Ui.TypeName(t))).ToList(), type),
                new("tag", "Tag", tags.Select(t => new FilterOption(t.Id.ToString(), t.Name)).ToList(), tagText),
            ],
            Keep = keep,
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        Crumbs(crumbs.ToArray());
        return Page();
    }
}

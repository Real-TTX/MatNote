using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Admin.Users;

public sealed record UserRow(long Id, string DisplayName, string Username, UserRole Role, bool IsActive, bool Locked, bool Totp, DateTime? LastLogin, int Notes);

/// <summary>The accounts. An administrator manages accounts (who, which role, enabled or not, two-step verification) and sees how many notes each has: never what is in them.</summary>
public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<UserRow> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        var q = ListQuery.Text(Request, "q");
        var role = ListQuery.Text(Request, "role");
        var status = ListQuery.Text(Request, "status");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var descending = ListQuery.Descending(Request);

        var all = db.Users.AsNoTracking();
        var total = await all.CountAsync();
        var query = all;
        if (role is "Admin" or "User")
        {
            var r = Enum.Parse<UserRole>(role);
            query = query.Where(u => u.Role == r);
        }
        else
        {
            role = null;
        }

        switch (status)
        {
            case "active":
                query = query.Where(u => u.IsActive);
                break;
            case "disabled":
                query = query.Where(u => !u.IsActive);
                break;
            case "totp":
                query = query.Where(u => u.TotpEnabled);
                break;
            default:
                status = null;
                break;
        }

        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern) || EF.Functions.ILike(u.Username, pattern) || (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        var rows = query.Select(u => new
        {
            u.Id, u.DisplayName, u.Username, u.Role, u.IsActive, Locked = u.LockoutUntil > now, Totp = u.TotpEnabled, LastLogin = u.LastLoginDate,
            Notes = db.Notes.Count(n => n.OwnerId == u.Id),
        });
        rows = (sort, descending) switch
        {
            ("login", false) => rows.OrderBy(r => r.LastLogin),
            ("login", true) => rows.OrderByDescending(r => r.LastLogin),
            ("username", false) => rows.OrderBy(r => r.Username),
            ("username", true) => rows.OrderByDescending(r => r.Username),
            (_, true) => rows.OrderByDescending(r => r.DisplayName),
            _ => rows.OrderBy(r => r.DisplayName),
        };

        var matching = await rows.CountAsync();
        var page = Math.Min(ListQuery.Page(Request), Math.Max(1, (int)Math.Ceiling(matching / (double)PageSize)));
        Items = (await rows.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync()).Select(r => new UserRow(r.Id, r.DisplayName, r.Username, r.Role, r.IsActive, r.Locked, r.Totp, r.LastLogin, r.Notes)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "users",
            Action = "/Admin/Users",
            Label = "Users",
            Search = q,
            SearchField = true,
            SearchPlaceholder = "Search name, user name or e-mail",
            Noun = "users",
            Total = total,
            Matching = matching,
            Sort = sort,
            Descending = descending,
            DefaultSort = "name",
            SortOptions = [new("name", "Name"), new("username", "User name"), new("login", "Last sign-in")],
            Filters =
            [
                new("role", "Role", [new("Admin", "Administrators"), new("User", "Users")], role),
                new("status", "Status", [new("active", "Active"), new("disabled", "Disabled"), new("totp", "With two-step verification")], status),
            ],
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
        Crumbs(("Administration", null), ("Users", null));
    }
}

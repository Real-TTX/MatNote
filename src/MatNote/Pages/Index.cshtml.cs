using MatNote.Data;
using MatNote.Pages.Notes;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages;

public sealed record HomeNote(long Id, string Title, NoteType Type, DateTime Updated, bool Own, string OwnerName);

/// <summary>The start page: a few numbers, the notes that changed last and the favorites.</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser me, AccessService access, MenuService menu) : AppPageModel
{
    public MenuModel Counts { get; private set; } = new();

    public int Folders { get; private set; }

    public List<HomeNote> Recent { get; private set; } = [];

    public List<HomeNote> Favorites { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var id = me.RequiredId;
        Counts = await menu.GetAsync();
        Folders = await db.Folders.CountAsync(f => f.OwnerId == id);

        var visible = await access.VisibleNotesAsync();
        Recent = await visible.AsNoTracking().OrderByDescending(n => n.UpdateDate).Take(8)
            .Select(n => new HomeNote(n.Id, n.Title, n.Type, n.UpdateDate, n.OwnerId == id, n.Owner!.DisplayName)).ToListAsync();
        Favorites = await db.Notes.AsNoTracking().Where(n => n.OwnerId == id && n.IsFavorite).OrderByDescending(n => n.UpdateDate).Take(6)
            .Select(n => new HomeNote(n.Id, n.Title, n.Type, n.UpdateDate, true, string.Empty)).ToListAsync();
        ViewData["Title"] = "Home";
    }
}

using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Notes;

/// <summary>The search of the dialogs "Link to a note" (the editor) and "Add to a note": JSON with the notes the person may see, or with <c>mine</c> their own text notes (never more than twenty).</summary>
public sealed class LookupModel(AccessService access, CurrentUser me) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? q, long? exclude, bool mine = false)
    {
        var userId = me.RequiredId;
        var notes = await access.VisibleNotesAsync();
        if (exclude is { } self)
        {
            notes = notes.Where(n => n.Id != self);
        }

        if (mine)
        {
            // the notes a person can add a link to: their own text notes ("Add to a note")
            notes = notes.Where(n => n.OwnerId == userId && (n.Type == NoteType.RichText || n.Type == NoteType.PlainText || n.Type == NoteType.Markdown));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            notes = notes.Where(n => EF.Functions.ILike(n.Title, pattern));
        }

        var items = await notes.OrderByDescending(n => n.UpdateDate).Take(20)
            .Select(n => new { n.Id, n.Title, n.Type, Folder = n.Folder != null && n.OwnerId == userId ? n.Folder.Name : null })
            .ToListAsync();
        return new JsonResult(items.Select(n => new { id = n.Id, title = n.Title, icon = Ui.TypeIcon(n.Type), folder = n.Folder }));
    }
}

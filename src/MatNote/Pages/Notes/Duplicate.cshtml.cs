using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Notes;

/// <summary>Duplicates a note of the person (no page of its own, JSON back): "(copy)" after the title, same folder, tags and attachments with it.</summary>
public sealed class DuplicateModel(CurrentUser me, DuplicateService duplicates) : PageModel
{
    public async Task<IActionResult> OnPostAsync(long id)
    {
        var copy = await duplicates.DuplicateNoteAsync(id, me.RequiredId);
        return copy is null ? NotFound() : new JsonResult(new { ok = true, id = copy.Id, title = copy.Title });
    }
}

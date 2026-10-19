using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Folders;

/// <summary>Duplicates a folder of the person with everything inside it, next to it (no page of its own, JSON back).</summary>
public sealed class DuplicateModel(CurrentUser me, DuplicateService duplicates) : PageModel
{
    public async Task<IActionResult> OnPostAsync(long id)
    {
        var folderId = await duplicates.DuplicateFolderAsync(id, me.RequiredId);
        return folderId is null ? NotFound() : new JsonResult(new { ok = true, id = folderId });
    }
}

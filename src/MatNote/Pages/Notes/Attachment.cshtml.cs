using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace MatNote.Pages.Notes;

/// <summary>
/// The attachments of a note, for the scripts of the page (no page of its own): download one (GET, always as a file, never shown in the page), add files (POST, JSON back)
/// and remove one (POST). The note decides: read to download, edit to add and remove.
/// </summary>
public sealed class AttachmentModel(AccessService access, AttachmentService attachments) : PageModel
{
    public async Task<IActionResult> OnGetAsync(long id, long file)
    {
        var (note, _) = await access.FindNoteAsync(id);
        if (note is null)
        {
            return NotFound();
        }

        var found = await attachments.FileAsync(note.Id, file);
        return found is null ? NotFound() : Deliver(this, found.Value.Name, found.Value.Data);
    }

    public async Task<IActionResult> OnPostUploadAsync(long id, List<IFormFile> files)
    {
        var (note, _) = await access.FindNoteAsync(id, Access.Edit);
        if (note is null)
        {
            return NotFound();
        }

        var added = new List<object>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            var (item, error) = await attachments.AddAsync(note.Id, file);
            if (item is null)
            {
                errors.Add(error ?? "The file could not be saved.");
                continue;
            }

            added.Add(Json(item, note.Id));
        }

        return new JsonResult(new { ok = errors.Count == 0, added, errors });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, long file)
    {
        var (note, _) = await access.FindNoteAsync(id, Access.Edit);
        if (note is null || !await attachments.DeleteAsync(note.Id, file))
        {
            return NotFound();
        }

        return new JsonResult(new { ok = true });
    }

    private static object Json(AttachmentView a, long noteId) => new { id = a.Id, name = a.FileName, size = Ui.Size(a.Size), icon = a.Icon, url = $"/Notes/Attachment?id={noteId}&file={a.Id}" };

    /// <summary>An attachment is always a download of the type <c>application/octet-stream</c>: what somebody uploaded never runs in the page of the app.</summary>
    public static IActionResult Deliver(PageModel page, string name, byte[] bytes)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(name);
        page.Response.Headers.ContentDisposition = disposition.ToString();
        page.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return page.File(bytes, "application/octet-stream");
    }
}

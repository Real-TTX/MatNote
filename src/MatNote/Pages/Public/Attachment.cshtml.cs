using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Public;

/// <summary>An attachment of a note inside a link. Like every download of a link it needs a link that allows exports.</summary>
public sealed class AttachmentModel(PublicShareAccess pub, AttachmentService attachments) : PageModel
{
    [FromRoute] public string Token { get; set; } = string.Empty;

    [FromRoute] public long NoteId { get; set; }

    [FromRoute] public long FileId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (status, share) = await pub.ResolveAsync(HttpContext, Token);
        if (status != PublicShareAccess.Status.Ok || share is null || !share.AllowExport)
        {
            return NotFound();
        }

        var note = await pub.NoteAsync(share, NoteId);
        if (note is null)
        {
            return NotFound();
        }

        var found = await attachments.FileAsync(note.Id, FileId);
        return found is null ? NotFound() : Notes.AttachmentModel.Deliver(this, found.Value.Name, found.Value.Data);
    }
}

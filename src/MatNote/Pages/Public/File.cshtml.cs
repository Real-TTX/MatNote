using MatNote.Data;
using MatNote.Pages.Notes;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Public;

/// <summary>The file of a note inside a link. A PDF may be shown in the page; every download needs a link that allows exports.</summary>
public sealed class FileModel(PublicShareAccess pub, NoteService notes) : PageModel
{
    [FromRoute] public string Token { get; set; } = string.Empty;

    [FromRoute] public long NoteId { get; set; }

    public async Task<IActionResult> OnGetAsync(bool inline = false)
    {
        var (status, share) = await pub.ResolveAsync(HttpContext, Token);
        if (status != PublicShareAccess.Status.Ok || share is null)
        {
            return NotFound();
        }

        var note = await pub.NoteAsync(share, NoteId);
        if (note is null || Ui.IsText(note.Type))
        {
            return NotFound();
        }

        var inlinePdf = inline && note.Type == NoteType.Pdf;
        if (!inlinePdf && !share.AllowExport)
        {
            return NotFound();
        }

        var bytes = await notes.FileBytesAsync(note.Id);
        return bytes is null ? NotFound() : Notes.FileModel.Deliver(this, note, bytes, inlinePdf);
    }
}

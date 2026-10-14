using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace MatNote.Pages.Notes;

/// <summary>The file of a note. Only a PDF is shown in the page (<c>inline=true</c>); every other file is a download, so a file that somebody uploaded never runs in the page of the app.</summary>
public sealed class FileModel(AccessService access, NoteService notes) : PageModel
{
    public async Task<IActionResult> OnGetAsync(long id, bool inline = false)
    {
        var (note, _) = await access.FindNoteAsync(id);
        if (note is null || Ui.IsText(note.Type))
        {
            return NotFound();
        }

        var bytes = await notes.FileBytesAsync(note.Id);
        return bytes is null ? NotFound() : Deliver(this, note, bytes, inline);
    }

    /// <summary>Answers with the bytes of a file note: a PDF may be shown in the page, everything else is an attachment of the type <c>application/octet-stream</c>.</summary>
    public static IActionResult Deliver(PageModel page, Note note, byte[] bytes, bool inline)
    {
        var name = string.IsNullOrWhiteSpace(note.FileName) ? note.Title : note.FileName;
        var showInPage = inline && note.Type == NoteType.Pdf;
        var disposition = new ContentDispositionHeaderValue(showInPage ? "inline" : "attachment");
        disposition.SetHttpFileName(name);
        page.Response.Headers.ContentDisposition = disposition.ToString();
        return page.File(bytes, note.Type == NoteType.Pdf ? "application/pdf" : "application/octet-stream");
    }
}

using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Notes;

/// <summary>Downloads a note in the chosen format (with its attachments in a zip when asked). Linked notes are copied in only when the person may read them (<see cref="NoteResolver"/>).</summary>
public sealed class ExportModel(AccessService access, INoteExporter exporter) : PageModel
{
    public async Task<IActionResult> OnGetAsync(long id, ExportFormat format, bool linked = false, bool attachments = false)
    {
        var (note, _) = await access.FindNoteAsync(id);
        if (note is null)
        {
            return NotFound();
        }

        if (!exporter.FormatsFor(note.Type).Contains(format))
        {
            return BadRequest("This note cannot be exported in that format.");
        }

        var result = await exporter.ExportAsync(note, format, new ExportOptions(linked, IncludeAttachments: attachments), async noteId => (await access.FindNoteAsync(noteId)).Note, HttpContext.RequestAborted);
        return File(result.Data, result.ContentType, result.FileName);
    }
}

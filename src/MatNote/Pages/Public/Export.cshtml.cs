using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Public;

/// <summary>Exports a note of a link. Linked notes are copied in only when they are inside the same link: nothing else of the owner leaves.</summary>
public sealed class ExportModel(PublicShareAccess pub, INoteExporter exporter) : PageModel
{
    [FromRoute] public string Token { get; set; } = string.Empty;

    [FromRoute] public long NoteId { get; set; }

    public async Task<IActionResult> OnGetAsync(Services.Export.ExportFormat format, bool linked = false, bool attachments = false)
    {
        var (status, share) = await pub.ResolveAsync(HttpContext, Token);
        if (status != PublicShareAccess.Status.Ok || share is null || !share.AllowExport)
        {
            return NotFound();
        }

        var note = await pub.NoteAsync(share, NoteId);
        if (note is null || !exporter.FormatsFor(note.Type).Contains(format))
        {
            return NotFound();
        }

        var result = await exporter.ExportAsync(note, format, new ExportOptions(linked, IncludeAttachments: attachments), id => pub.NoteAsync(share, id), HttpContext.RequestAborted);
        return File(result.Data, result.ContentType, result.FileName);
    }
}

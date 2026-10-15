using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Notes;

public sealed record LinkedNote(long Id, string Title, NoteType Type);

/// <summary>A note on a page: its text or file, what it links to, what links to it, and the actions (edit, copy, export, share, favorite).</summary>
public sealed class ViewModel(AppDbContext db, CurrentUser me, AccessService access, INotePreview preview, INoteExporter exporter, FolderService folders) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    public Note Note { get; private set; } = null!;

    public Access Access { get; private set; }

    /// <summary>The text of the note as HTML (safe), or <c>null</c> for a file.</summary>
    public string? BodyHtml { get; private set; }

    /// <summary>The preview of a Word or Excel file as HTML (safe).</summary>
    public string? FileHtml { get; private set; }

    public string OwnerName { get; private set; } = string.Empty;

    public string? FolderName { get; private set; }

    public List<TagView> Tags { get; private set; } = [];

    public List<LinkedNote> LinksTo { get; private set; } = [];

    public List<LinkedNote> LinkedFrom { get; private set; } = [];

    public IReadOnlyList<ExportFormat> Formats { get; private set; } = [];

    public bool IsOwner => Access == Access.Owner;

    public async Task<IActionResult> OnGetAsync()
    {
        var (note, level) = await access.FindNoteAsync(Id);
        if (note is null)
        {
            return NotFound();
        }

        await FillAsync(note, level);
        return Page();
    }

    /// <summary>
    /// The text of a note as JSON, for the button of a link that shows the linked note inside this one (notelinks.js). Only what the person may read; a file has no text to show.
    /// </summary>
    public async Task<IActionResult> OnGetBodyAsync()
    {
        var (note, _) = await access.FindNoteAsync(Id);
        if (note is null)
        {
            return NotFound();
        }

        var html = Ui.IsText(note.Type) ? preview.RenderText(note, id => $"/Notes/View?id={id}") : null;
        return new JsonResult(new { id = note.Id, title = note.Title, icon = Ui.TypeIcon(note.Type), html });
    }

    public async Task<IActionResult> OnPostFavoriteAsync()
    {
        var (note, level) = await access.FindNoteAsync(Id, Access.Owner, track: true);
        if (note is null || level != Access.Owner)
        {
            return NotFound();
        }

        note.IsFavorite = !note.IsFavorite;
        await db.SaveChangesAsync();
        Flash(note.IsFavorite ? "Added to your favorites." : "Removed from your favorites.");
        return RedirectToPage(new { id = Id, embed = EmbedValue });
    }

    private async Task FillAsync(Note note, Access level)
    {
        Note = note;
        ViewData["NoteId"] = note.Id;
        Access = level;
        OwnerName = await db.Users.AsNoTracking().Where(u => u.Id == note.OwnerId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? string.Empty;

        if (Ui.IsText(note.Type))
        {
            BodyHtml = preview.RenderText(note, id => $"/Notes/View?id={id}");
        }
        else if (note.Type is NoteType.Docx or NoteType.Xlsx)
        {
            FileHtml = await preview.RenderFileAsync(note);
        }

        Formats = exporter.FormatsFor(note.Type);

        if (IsOwner)
        {
            Tags = await db.NoteTags.AsNoTracking().Where(nt => nt.NoteId == note.Id).OrderBy(nt => nt.Tag!.Name).Select(nt => new TagView(nt.Tag!.Name, nt.Tag.Color, nt.TagId)).ToListAsync();
            if (note.FolderId is { } folderId)
            {
                var path = await folders.PathAsync(folderId, me.RequiredId);
                FolderName = string.Join(" / ", path.Select(p => p.Name));
                ViewData["ActiveFolder"] = folderId;
            }
        }

        var visible = await access.VisibleNotesAsync();
        LinksTo = await visible.Where(n => db.NoteLinks.Any(l => l.SourceNoteId == note.Id && l.TargetNoteId == n.Id))
            .OrderBy(n => n.Title).Select(n => new LinkedNote(n.Id, n.Title, n.Type)).ToListAsync();
        LinkedFrom = await visible.Where(n => db.NoteLinks.Any(l => l.TargetNoteId == note.Id && l.SourceNoteId == n.Id))
            .OrderBy(n => n.Title).Select(n => new LinkedNote(n.Id, n.Title, n.Type)).ToListAsync();

        ViewData["Title"] = note.Title;
        ViewData["ActiveNav"] = IsOwner ? "all" : "shares";
        var crumbs = new List<(string, string?)> { ("Notes", "/Notes") };
        if (IsOwner && note.FolderId is { } f)
        {
            var path = await folders.PathAsync(f, me.RequiredId);
            crumbs.AddRange(path.Select(p => (p.Name, (string?)$"/Notes?folder={p.Id}")));
        }

        crumbs.Add((note.Title, null));
        Crumbs(crumbs.ToArray());
    }

    public static string FormatName(ExportFormat format) => format switch
    {
        ExportFormat.Original => "The file as it is",
        ExportFormat.Txt => "Plain text (.txt)",
        ExportFormat.Markdown => "Markdown (.md)",
        ExportFormat.Html => "Web page (.html)",
        ExportFormat.Rtf => "Rich Text Format (.rtf)",
        ExportFormat.Docx => "Word document (.docx)",
        ExportFormat.Pdf => "PDF (.pdf)",
        ExportFormat.Csv => "Table (.csv)",
        ExportFormat.Json => "Data (.json)",
        _ => format.ToString(),
    };
}

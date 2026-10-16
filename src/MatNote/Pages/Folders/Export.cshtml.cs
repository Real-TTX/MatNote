using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Folders;

/// <summary>Exports a folder with everything inside it as one zip: the notes in the chosen format, in directories like the folders.</summary>
public sealed class ExportModel(AppDbContext db, AccessService access, FolderService folders, INoteExporter exporter) : AppPageModel
{
    public static readonly ExportFormat[] Formats = [ExportFormat.Markdown, ExportFormat.Txt, ExportFormat.Html, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf];

    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    [BindProperty(SupportsGet = true)] public ExportFormat Format { get; set; } = ExportFormat.Markdown;

    [BindProperty(SupportsGet = true)] public bool Linked { get; set; } = true;

    /// <summary>The files attached to the notes go along: a directory next to each note that has some.</summary>
    [BindProperty(SupportsGet = true)] public bool Attachments { get; set; } = true;

    public string FolderName { get; private set; } = string.Empty;

    public int NoteCount { get; private set; }

    public List<Choice> FormatChoices { get; } = Formats.Select(f => new Choice(f.ToString(), Notes.ViewModel.FormatName(f))).ToList();

    public async Task<IActionResult> OnGetAsync()
    {
        var (folder, _) = await access.FindFolderAsync(Id);
        if (folder is null)
        {
            return NotFound();
        }

        var items = await ItemsAsync(folder);
        FolderName = folder.Name;
        NoteCount = items.Count;
        ViewData["Title"] = "Export a folder";
        ViewData["ActiveNav"] = "folders";
        Crumbs(("Folders", "/Folders"), (folder.Name, $"/Notes?folder={folder.Id}"), ("Export", null));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (folder, _) = await access.FindFolderAsync(Id);
        if (folder is null || !Formats.Contains(Format))
        {
            return NotFound();
        }

        var items = await ItemsAsync(folder);
        var result = await exporter.ExportManyAsync(folder.Name, items, Format, new ExportOptions(Linked, IncludeAttachments: Attachments), async noteId => (await access.FindNoteAsync(noteId)).Note, HttpContext.RequestAborted);
        return File(result.Data, result.ContentType, result.FileName);
    }

    /// <summary>The notes of the folder and its subfolders that the person may read, each with the path of its folder inside the zip.</summary>
    private async Task<List<(string Folder, Note Note)>> ItemsAsync(Folder root)
    {
        var refs = await folders.RefsAsync(root.OwnerId);
        var ids = FolderTree.SelfAndDescendants(refs, root.Id);
        var names = await db.Folders.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name);
        var byParent = refs.ToDictionary(r => r.Id, r => r.ParentId);

        string PathOf(long id)
        {
            var parts = new List<string>();
            long? at = id;
            while (at is { } current && current != root.Id && names.TryGetValue(current, out var name) && parts.Count < FolderService.MaxDepth + 2)
            {
                parts.Insert(0, name);
                at = byParent.GetValueOrDefault(current);
            }

            return string.Join("/", parts);
        }

        var visible = await access.VisibleNotesAsync();
        var notes = await visible.AsNoTracking().Where(n => n.FolderId != null && ids.Contains(n.FolderId.Value)).OrderBy(n => n.Title).ToListAsync();
        return notes.Select(n => (PathOf(n.FolderId!.Value), n)).ToList();
    }
}

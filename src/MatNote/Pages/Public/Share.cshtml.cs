using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Public;

public sealed record PublicNote(long Id, string Title, NoteType Type, DateTime Updated);

public sealed record PublicFolder(long Id, string Name);

/// <summary>
/// A shared link, opened by anyone who has it. It shows one note, or a folder with its notes (and the notes in it), read only. It gives away nothing else:
/// not who else has access, not the folders around it, not the other notes of the owner.
/// </summary>
public sealed class ShareModel(PublicShareAccess pub, AppDbContext db, INotePreview preview, INoteExporter exporter) : PageModel
{
    [FromRoute] public string Token { get; set; } = string.Empty;

    [FromRoute] public long? NoteId { get; set; }

    [BindProperty(SupportsGet = true)] public long? Folder { get; set; }

    public Share Share { get; private set; } = null!;

    public Note? Note { get; private set; }

    public string? BodyHtml { get; private set; }

    public string? FileHtml { get; private set; }

    public string OwnerName { get; private set; } = string.Empty;

    public string Heading { get; private set; } = string.Empty;

    public List<PublicNote> Notes { get; private set; } = [];

    public List<PublicFolder> Subfolders { get; private set; } = [];

    public List<PublicFolder> Path { get; private set; } = [];

    public IReadOnlyList<ExportFormat> Formats { get; private set; } = [];

    public string BaseUrl => "/s/" + Token;

    public async Task<IActionResult> OnGetAsync()
    {
        var (status, share) = await pub.ResolveAsync(HttpContext, Token);
        if (status == PublicShareAccess.Status.NotFound)
        {
            return NotFound();
        }

        if (status == PublicShareAccess.Status.NeedsPassword)
        {
            return Redirect(BaseUrl + "/unlock");
        }

        Share = share!;
        OwnerName = await db.Users.AsNoTracking().Where(u => u.Id == Share.OwnerId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? string.Empty;
        ViewData["SharedBy"] = OwnerName;

        if (Share.NoteId is { } shared)
        {
            Note = await pub.NoteAsync(Share, shared);
        }
        else if (NoteId is { } asked)
        {
            Note = await pub.NoteAsync(Share, asked);
            if (Note is null)
            {
                return NotFound();
            }
        }

        if (Note is not null)
        {
            await ShowNoteAsync(Note);
        }
        else
        {
            await ShowFolderAsync();
        }

        if (NoteId is null && Folder is null)
        {
            await pub.CountViewAsync(Share);
        }

        ViewData["Title"] = Heading;
        return Page();
    }

    private async Task ShowNoteAsync(Note note)
    {
        Heading = note.Title;
        ViewData["PageSub"] = $"{Ui.TypeName(note.Type)} · changed {note.UpdateDate:yyyy-MM-dd}";
        if (Ui.IsText(note.Type))
        {
            BodyHtml = preview.RenderText(note, id => $"{BaseUrl}/{id}");
        }
        else if (note.Type is NoteType.Docx or NoteType.Xlsx)
        {
            FileHtml = await preview.RenderFileAsync(note);
        }

        Formats = Share.AllowExport ? exporter.FormatsFor(note.Type) : [];
        if (Share.FolderId is not null && note.FolderId is { } folder)
        {
            Folder = folder;
            Path = await PathAsync(folder);
        }
    }

    private async Task ShowFolderAsync()
    {
        var root = Share.FolderId!.Value;
        var folder = await pub.FolderAsync(Share, Folder ?? root);
        if (folder is null)
        {
            folder = await pub.FolderAsync(Share, root);
        }

        Folder = folder!.Id;
        Heading = folder.Name;
        ViewData["PageSub"] = "A shared folder";
        Path = await PathAsync(folder.Id);
        Subfolders = await db.Folders.AsNoTracking().Where(f => f.ParentId == folder.Id && f.OwnerId == Share.OwnerId).OrderBy(f => f.Name).Select(f => new PublicFolder(f.Id, f.Name)).ToListAsync();
        Notes = await db.Notes.AsNoTracking().Where(n => n.FolderId == folder.Id && n.OwnerId == Share.OwnerId).OrderBy(n => n.Title)
            .Select(n => new PublicNote(n.Id, n.Title, n.Type, n.UpdateDate)).ToListAsync();
    }

    /// <summary>The folders from the shared one down to <paramref name="folderId"/> (never the folders above the shared one).</summary>
    private async Task<List<PublicFolder>> PathAsync(long folderId)
    {
        var root = Share.FolderId!.Value;
        var all = await db.Folders.AsNoTracking().Where(f => f.OwnerId == Share.OwnerId).Select(f => new { f.Id, f.ParentId, f.Name }).ToListAsync();
        var byId = all.ToDictionary(f => f.Id);
        var path = new List<PublicFolder>();
        long? at = folderId;
        while (at is { } current && byId.TryGetValue(current, out var f) && path.Count < FolderService.MaxDepth + 2)
        {
            path.Insert(0, new PublicFolder(f.Id, f.Name));
            if (current == root)
            {
                break;
            }

            at = f.ParentId;
        }

        return path;
    }
}

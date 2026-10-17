using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Public;

public sealed record PublicNote(long Id, string Title, NoteType Type, DateTime Updated);

public sealed record PublicFolder(long Id, string Name);

/// <summary>A folder as a row of the list (like in a file explorer): how many folders and notes are directly inside.</summary>
public sealed record PublicFolderRow(long Id, string Name, int Folders, int Notes);

/// <summary>A folder of the tree at the left: the shared folder is the top, only folders inside it are in it.</summary>
public sealed class PublicTreeNode(long id, string name)
{
    public long Id { get; } = id;

    public string Name { get; } = name;

    public List<PublicTreeNode> Children { get; } = [];
}

/// <summary>
/// A shared link, opened by anyone who has it. It shows one note, or a folder with its notes (and the notes in it), read only. It gives away nothing else:
/// not who else has access, not the folders around it, not the other notes of the owner.
/// </summary>
public sealed class ShareModel(PublicShareAccess pub, AppDbContext db, INotePreview preview, INoteExporter exporter, AttachmentService attachments) : PageModel
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

    public List<PublicFolderRow> Subfolders { get; private set; } = [];

    /// <summary>The folders of the share as a tree (the shared folder at the top); <c>null</c> for a link to one note.</summary>
    public PublicTreeNode? Tree { get; private set; }

    public List<PublicFolder> Path { get; private set; } = [];

    public IReadOnlyList<ExportFormat> Formats { get; private set; } = [];

    public IReadOnlyList<AttachmentView> Attachments { get; private set; } = [];

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
        var firstVisit = NoteId is null && Folder is null;
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

        if (Share.FolderId is not null)
        {
            await BuildTreeAsync();
        }

        if (firstVisit)
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
        Attachments = await attachments.ListAsync(note.Id);
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
        Notes = await db.Notes.AsNoTracking().Where(n => n.FolderId == folder.Id && n.OwnerId == Share.OwnerId).OrderBy(n => n.Title)
            .Select(n => new PublicNote(n.Id, n.Title, n.Type, n.UpdateDate)).ToListAsync();
    }

    /// <summary>The tree of the folders inside the share, and the rows (the folders directly in the open folder, with how much is inside them).</summary>
    private async Task BuildTreeAsync()
    {
        var root = Share.FolderId!.Value;
        var all = await db.Folders.AsNoTracking().Where(f => f.OwnerId == Share.OwnerId).Select(f => new { f.Id, f.ParentId, f.Name }).ToListAsync();
        var children = all.Where(f => f.ParentId is not null).ToLookup(f => f.ParentId!.Value);
        var rootFolder = all.FirstOrDefault(f => f.Id == root);
        if (rootFolder is null)
        {
            return;
        }

        var nodes = new Dictionary<long, PublicTreeNode>();
        PublicTreeNode Build(long id, string name, int depth)
        {
            var node = new PublicTreeNode(id, name);
            nodes[id] = node;
            if (depth < FolderService.MaxDepth + 2)
            {
                foreach (var child in children[id].OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!nodes.ContainsKey(child.Id))
                    {
                        node.Children.Add(Build(child.Id, child.Name, depth + 1));
                    }
                }
            }

            return node;
        }

        Tree = Build(root, rootFolder.Name, 0);
        if (Note is not null || Folder is not { } open || !nodes.TryGetValue(open, out var current))
        {
            return;
        }

        var counts = await db.Notes.AsNoTracking().Where(n => n.OwnerId == Share.OwnerId && n.FolderId != null && nodes.Keys.Contains(n.FolderId.Value))
            .GroupBy(n => n.FolderId!.Value).Select(g => new { Folder = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Folder, g => g.Count);
        Subfolders = current.Children.Select(c => new PublicFolderRow(c.Id, c.Name, c.Children.Count, counts.GetValueOrDefault(c.Id))).ToList();
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

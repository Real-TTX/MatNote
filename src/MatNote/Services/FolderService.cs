using MatNote.Controls;
using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>Folders: the tree of one person, names with their path, moving without making a loop, and deleting a folder with everything inside it.</summary>
public sealed class FolderService(AppDbContext db)
{
    public const int MaxDepth = 12;

    public async Task<List<FolderRef>> RefsAsync(long ownerId)
        => await db.Folders.AsNoTracking().Where(f => f.OwnerId == ownerId).Select(f => new FolderRef(f.Id, f.ParentId)).ToListAsync();

    /// <summary>The folders of a person as choices for a picker: "Work / Prompts", sorted by path. <paramref name="exceptSubtreeOf"/> leaves out a folder and everything below it (the parent choices of that folder).</summary>
    public async Task<List<Choice>> ChoicesAsync(long ownerId, long? exceptSubtreeOf = null)
    {
        var folders = await db.Folders.AsNoTracking().Where(f => f.OwnerId == ownerId).Select(f => new { f.Id, f.ParentId, f.Name, f.Color }).ToListAsync();
        var byId = folders.ToDictionary(f => f.Id);
        var excluded = exceptSubtreeOf is { } root ? FolderTree.SelfAndDescendants(folders.Select(f => new FolderRef(f.Id, f.ParentId)).ToList(), root) : [];

        string PathOf(long id)
        {
            var names = new List<string>();
            long? at = id;
            while (at is { } current && byId.TryGetValue(current, out var f) && names.Count < MaxDepth + 2)
            {
                names.Insert(0, f.Name);
                at = f.ParentId;
            }

            return string.Join(" / ", names);
        }

        return folders.Where(f => !excluded.Contains(f.Id))
            .Select(f => new Choice(f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), PathOf(f.Id), null, f.Color))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The path from the top folder to <paramref name="folderId"/> as (id, name) pairs, for a breadcrumb.</summary>
    public async Task<List<(long Id, string Name)>> PathAsync(long folderId, long ownerId)
    {
        var folders = await db.Folders.AsNoTracking().Where(f => f.OwnerId == ownerId).Select(f => new { f.Id, f.ParentId, f.Name }).ToListAsync();
        var byId = folders.ToDictionary(f => f.Id);
        var path = new List<(long, string)>();
        long? at = folderId;
        while (at is { } current && byId.TryGetValue(current, out var f) && path.Count < MaxDepth + 2)
        {
            path.Insert(0, (f.Id, f.Name));
            at = f.ParentId;
        }

        return path;
    }

    /// <summary>The depth of a folder (a top folder is 1).</summary>
    public async Task<int> DepthAsync(long folderId, long ownerId) => (await PathAsync(folderId, ownerId)).Count;

    /// <summary>
    /// Deletes a folder, its subfolders and the notes in all of them softly: the notes land in the trash (where they can be restored, into no folder), the shares of the folders end.
    /// </summary>
    public async Task<int> DeleteTreeAsync(Folder folder)
    {
        var refs = await RefsAsync(folder.OwnerId);
        var ids = FolderTree.SelfAndDescendants(refs, folder.Id);
        var notes = await db.Notes.Where(n => n.FolderId != null && ids.Contains(n.FolderId.Value)).ToListAsync();
        foreach (var note in notes)
        {
            db.Notes.Remove(note);
        }

        var folders = await db.Folders.Where(f => ids.Contains(f.Id)).ToListAsync();
        foreach (var f in folders)
        {
            db.Folders.Remove(f);
        }

        var shares = await db.Shares.Where(s => s.FolderId != null && ids.Contains(s.FolderId.Value)).ToListAsync();
        foreach (var share in shares)
        {
            db.Shares.Remove(share);
        }

        await db.SaveChangesAsync();
        return notes.Count;
    }
}

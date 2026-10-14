using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

public enum Access
{
    None = 0,
    Read = 1,
    Edit = 2,
    Owner = 3,
}

/// <summary>What is shared with one person, folders already expanded to everything inside them.</summary>
public sealed record SharedScope(IReadOnlyDictionary<long, SharePermission> Notes, IReadOnlyDictionary<long, SharePermission> Folders)
{
    public static readonly SharedScope Empty = new(new Dictionary<long, SharePermission>(), new Dictionary<long, SharePermission>());

    public Access For(Note note)
    {
        var best = Access.None;
        if (Notes.TryGetValue(note.Id, out var direct))
        {
            best = Max(best, direct);
        }

        if (note.FolderId is { } folder && Folders.TryGetValue(folder, out var viaFolder))
        {
            best = Max(best, viaFolder);
        }

        return best;
    }

    public Access For(Folder folder) => Folders.TryGetValue(folder.Id, out var p) ? Max(Access.None, p) : Access.None;

    private static Access Max(Access a, SharePermission p)
    {
        var other = p == SharePermission.Edit ? Access.Edit : Access.Read;
        return other > a ? other : a;
    }
}

/// <summary>
/// Who may see and change what. The rules are few and strict: a note and a folder belong to one person (the owner), and the owner may do everything.
/// Another person has only what is shared with them: Read (see, copy, export) or Edit (also change title and content), for a note or for a folder
/// with everything inside it. An administrator has no access to the notes of others: the user management is not a way to read them.
/// </summary>
public sealed class AccessService(AppDbContext db, CurrentUser current)
{
    private SharedScope? _scope;

    /// <summary>What is shared with the signed-in user (loaded once per request).</summary>
    public async Task<SharedScope> ScopeAsync()
    {
        if (_scope is not null)
        {
            return _scope;
        }

        _scope = current.Id is { } id ? await LoadScopeAsync(db, id) : SharedScope.Empty;
        return _scope;
    }

    public static async Task<SharedScope> LoadScopeAsync(AppDbContext db, long userId)
    {
        var now = DateTime.UtcNow;
        var shares = await db.Shares.AsNoTracking()
            .Where(s => s.Kind == ShareKind.User && s.SharedWithUserId == userId && (s.ExpiresDate == null || s.ExpiresDate > now))
            .Select(s => new { s.NoteId, s.FolderId, s.Permission, s.OwnerId })
            .ToListAsync();
        if (shares.Count == 0)
        {
            return SharedScope.Empty;
        }

        var notes = new Dictionary<long, SharePermission>();
        var folders = new Dictionary<long, SharePermission>();
        foreach (var s in shares.Where(s => s.NoteId is not null))
        {
            Raise(notes, s.NoteId!.Value, s.Permission);
        }

        var folderShares = shares.Where(s => s.FolderId is not null).ToList();
        if (folderShares.Count > 0)
        {
            var owners = folderShares.Select(s => s.OwnerId).Distinct().ToList();
            var all = await db.Folders.AsNoTracking().Where(f => owners.Contains(f.OwnerId)).Select(f => new FolderRef(f.Id, f.ParentId)).ToListAsync();
            foreach (var s in folderShares)
            {
                foreach (var id in FolderTree.SelfAndDescendants(all, s.FolderId!.Value))
                {
                    Raise(folders, id, s.Permission);
                }
            }
        }

        return new SharedScope(notes, folders);
    }

    private static void Raise(Dictionary<long, SharePermission> map, long id, SharePermission permission)
    {
        if (!map.TryGetValue(id, out var old) || permission > old)
        {
            map[id] = permission;
        }
    }

    public async Task<Access> OfAsync(Note note)
    {
        if (current.Id is not { } id)
        {
            return Access.None;
        }

        return note.OwnerId == id ? Access.Owner : (await ScopeAsync()).For(note);
    }

    public async Task<Access> OfAsync(Folder folder)
    {
        if (current.Id is not { } id)
        {
            return Access.None;
        }

        return folder.OwnerId == id ? Access.Owner : (await ScopeAsync()).For(folder);
    }

    /// <summary>The notes the signed-in user may see: their own, and what is shared with them. Narrow it further before you list it.</summary>
    public async Task<IQueryable<Note>> VisibleNotesAsync()
    {
        var id = current.RequiredId;
        var scope = await ScopeAsync();
        var noteIds = scope.Notes.Keys.ToList();
        var folderIds = scope.Folders.Keys.ToList();
        return db.Notes.Where(n => n.OwnerId == id || noteIds.Contains(n.Id) || (n.FolderId != null && folderIds.Contains(n.FolderId.Value)));
    }

    /// <summary>The note, when the signed-in user may see it at least with <paramref name="needed"/>; otherwise <c>null</c> (the same answer for "does not exist").</summary>
    public async Task<(Note? Note, Access Access)> FindNoteAsync(long id, Access needed = Access.Read, bool track = false)
    {
        var query = track ? db.Notes : db.Notes.AsNoTracking();
        var note = await query.FirstOrDefaultAsync(n => n.Id == id);
        if (note is null)
        {
            return (null, Access.None);
        }

        var access = await OfAsync(note);
        return access >= needed ? (note, access) : (null, Access.None);
    }

    public async Task<(Folder? Folder, Access Access)> FindFolderAsync(long id, Access needed = Access.Read, bool track = false)
    {
        var query = track ? db.Folders : db.Folders.AsNoTracking();
        var folder = await query.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null)
        {
            return (null, Access.None);
        }

        var access = await OfAsync(folder);
        return access >= needed ? (folder, access) : (null, Access.None);
    }

    /// <summary>Is the note inside a share of a link (the note itself, or a folder with the note inside it, at any depth)?</summary>
    public static async Task<bool> LinkCoversAsync(AppDbContext db, Share share, Note note)
    {
        if (share.NoteId is { } shared)
        {
            return shared == note.Id;
        }

        if (share.FolderId is not { } root || note.FolderId is null)
        {
            return false;
        }

        var all = await db.Folders.AsNoTracking().Where(f => f.OwnerId == share.OwnerId).Select(f => new FolderRef(f.Id, f.ParentId)).ToListAsync();
        return FolderTree.SelfAndDescendants(all, root).Contains(note.FolderId.Value);
    }
}

public readonly record struct FolderRef(long Id, long? ParentId);

/// <summary>Walking a tree of folders (kept in memory: a person has a few hundred at most).</summary>
public static class FolderTree
{
    public static HashSet<long> SelfAndDescendants(IReadOnlyCollection<FolderRef> all, long root)
    {
        var children = all.Where(f => f.ParentId is not null).ToLookup(f => f.ParentId!.Value, f => f.Id);
        var result = new HashSet<long> { root };
        var queue = new Queue<long>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            foreach (var child in children[queue.Dequeue()])
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    /// <summary>The ids from the top folder down to <paramref name="id"/> (a cycle cannot happen, but the walk is bounded anyway).</summary>
    public static List<long> PathTo(IReadOnlyCollection<FolderRef> all, long id)
    {
        var byId = all.ToDictionary(f => f.Id);
        var path = new List<long>();
        long? at = id;
        while (at is { } current && byId.TryGetValue(current, out var folder) && path.Count < 64)
        {
            path.Insert(0, current);
            at = folder.ParentId;
        }

        return path;
    }
}

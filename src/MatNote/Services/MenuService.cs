using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>A folder of the menu with what is inside it.</summary>
public sealed class FolderNode
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public string? Color { get; init; }

    /// <summary>Notes directly in this folder.</summary>
    public int Count { get; set; }

    public List<FolderNode> Children { get; } = [];

    public bool Contains(long id) => Id == id || Children.Any(c => c.Contains(id));
}

/// <summary>What the menu on the left shows: the tree of the folders of the signed-in person and a few counts.</summary>
public sealed class MenuModel
{
    public List<FolderNode> Folders { get; init; } = [];

    public int Notes { get; init; }

    public int Favorites { get; init; }

    public int Tags { get; init; }

    public int Trash { get; init; }

    public int SharedWithMe { get; init; }
}

public sealed class MenuService(AppDbContext db, CurrentUser current)
{
    private MenuModel? _model;

    public async Task<MenuModel> GetAsync()
    {
        if (_model is not null)
        {
            return _model;
        }

        if (current.Id is not { } id)
        {
            return _model = new MenuModel();
        }

        var folders = await db.Folders.AsNoTracking().Where(f => f.OwnerId == id).OrderBy(f => f.Name).Select(f => new { f.Id, f.ParentId, f.Name, f.Color }).ToListAsync();
        var counts = await db.Notes.AsNoTracking().Where(n => n.OwnerId == id && n.FolderId != null).GroupBy(n => n.FolderId!.Value).Select(g => new { Folder = g.Key, Count = g.Count() }).ToListAsync();
        var nodes = folders.ToDictionary(f => f.Id, f => new FolderNode { Id = f.Id, Name = f.Name, Color = f.Color });
        foreach (var c in counts)
        {
            if (nodes.TryGetValue(c.Folder, out var node))
            {
                node.Count = c.Count;
            }
        }

        var roots = new List<FolderNode>();
        foreach (var f in folders)
        {
            if (f.ParentId is { } parent && nodes.TryGetValue(parent, out var parentNode))
            {
                parentNode.Children.Add(nodes[f.Id]);
            }
            else
            {
                roots.Add(nodes[f.Id]);
            }
        }

        var now = DateTime.UtcNow;
        _model = new MenuModel
        {
            Folders = roots,
            Notes = await db.Notes.CountAsync(n => n.OwnerId == id),
            Favorites = await db.Notes.CountAsync(n => n.OwnerId == id && n.IsFavorite),
            Tags = await db.Tags.CountAsync(t => t.OwnerId == id),
            Trash = await db.Notes.IgnoreQueryFilters().CountAsync(n => n.OwnerId == id && n.UpdateState == SoftEntity.Deleted),
            SharedWithMe = await db.Shares.CountAsync(s => s.Kind == ShareKind.User && s.SharedWithUserId == id && (s.ExpiresDate == null || s.ExpiresDate > now)),
        };
        return _model;
    }
}

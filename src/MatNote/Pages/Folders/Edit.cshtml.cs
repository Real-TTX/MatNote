using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Folders;

/// <summary>Create or change a folder. Delete is here, not in the list (LIST-08).</summary>
public sealed class EditModel(AppDbContext db, CurrentUser me, FolderService folders) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    /// <summary>The id of the parent folder; empty for a top folder.</summary>
    [BindProperty(SupportsGet = true)] public string Parent { get; set; } = string.Empty;

    [BindProperty] public string Color { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public int NoteCount { get; private set; }

    public int SubfolderCount { get; private set; }

    public List<Choice> ParentChoices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is not null)
        {
            var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == Id && f.OwnerId == me.RequiredId);
            if (folder is null)
            {
                return NotFound();
            }

            Name = folder.Name;
            Parent = folder.ParentId?.ToString() ?? string.Empty;
            Color = folder.Color ?? string.Empty;
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var owner = me.RequiredId;
        Folder? folder = null;
        if (Id is not null)
        {
            folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == Id && f.OwnerId == owner);
            if (folder is null)
            {
                return NotFound();
            }
        }

        Name = Name.Trim();
        if (Name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), "Please enter a name.");
        }
        else if (Name.Length > 120)
        {
            ModelState.AddModelError(nameof(Name), "The name is too long (120 characters at most).");
        }

        long? parentId = null;
        if (Parent.Length > 0)
        {
            var refs = await folders.RefsAsync(owner);
            if (!long.TryParse(Parent, out var p) || refs.All(r => r.Id != p))
            {
                ModelState.AddModelError(nameof(Parent), "Please choose one of your folders.");
            }
            else if (Id is { } self && FolderTree.SelfAndDescendants(refs, self).Contains(p))
            {
                ModelState.AddModelError(nameof(Parent), "A folder cannot be moved into itself or into one of its subfolders.");
            }
            else if (FolderTree.PathTo(refs, p).Count >= FolderService.MaxDepth)
            {
                ModelState.AddModelError(nameof(Parent), $"Folders can be nested {FolderService.MaxDepth} levels deep.");
            }
            else
            {
                parentId = p;
            }
        }

        if (Color.Length > 0 && !Ui.IsColour(Color))
        {
            ModelState.AddModelError(nameof(Color), "Please choose a colour.");
        }

        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return Page();
        }

        if (folder is null)
        {
            folder = new Folder { OwnerId = owner };
            db.Folders.Add(folder);
        }

        folder.Name = Name;
        folder.ParentId = parentId;
        folder.Color = Color.Length == 0 ? null : Color.ToLowerInvariant();
        await db.SaveChangesAsync();
        Flash($"Folder “{folder.Name}” is saved.");
        return IsNew ? RedirectToPage("/Notes/Index", new { folder = folder.Id }) : RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == Id && f.OwnerId == me.RequiredId);
        if (folder is null)
        {
            return NotFound();
        }

        var notes = await folders.DeleteTreeAsync(folder);
        Flash(notes > 0 ? $"Folder “{folder.Name}” is deleted. Its {notes} notes are in the trash." : $"Folder “{folder.Name}” is deleted.");
        return RedirectToPage("Index");
    }

    private async Task PrepareAsync()
    {
        var owner = me.RequiredId;
        ParentChoices = await folders.ChoicesAsync(owner, Id);
        if (Id is { } id)
        {
            var refs = await folders.RefsAsync(owner);
            var ids = FolderTree.SelfAndDescendants(refs, id);
            SubfolderCount = ids.Count - 1;
            NoteCount = await db.Notes.CountAsync(n => n.FolderId != null && ids.Contains(n.FolderId.Value));
        }

        ViewData["Title"] = IsNew ? "New folder" : "Edit folder";
        ViewData["ActiveNav"] = "folders";
        Crumbs(("Folders", "/Folders"), (IsNew ? "New folder" : Name, null));
    }
}

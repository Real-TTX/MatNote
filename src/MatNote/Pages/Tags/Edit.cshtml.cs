using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Tags;

/// <summary>Create or change a tag. Delete is here, not in the list (LIST-08).</summary>
public sealed class EditModel(AppDbContext db, CurrentUser me) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string Color { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public int NoteCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is not null)
        {
            var tag = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == Id && t.OwnerId == me.RequiredId);
            if (tag is null)
            {
                return NotFound();
            }

            Name = tag.Name;
            Color = tag.Color ?? string.Empty;
            NoteCount = await db.NoteTags.CountAsync(nt => nt.TagId == tag.Id);
        }

        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var owner = me.RequiredId;
        Tag? tag = null;
        if (Id is not null)
        {
            tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == Id && t.OwnerId == owner);
            if (tag is null)
            {
                return NotFound();
            }
        }

        Name = Name.Trim();
        if (Name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), "Please enter a name.");
        }
        else if (Name.Length > 60)
        {
            ModelState.AddModelError(nameof(Name), "The name is too long (60 characters at most).");
        }
        else if (await db.Tags.AnyAsync(t => t.OwnerId == owner && t.Id != Id && EF.Functions.ILike(t.Name, Name.Replace("%", "\\%").Replace("_", "\\_"))))
        {
            ModelState.AddModelError(nameof(Name), "You already have a tag with this name.");
        }

        if (Color.Length > 0 && !Ui.IsColour(Color))
        {
            ModelState.AddModelError(nameof(Color), "Please choose a colour.");
        }

        if (!ModelState.IsValid)
        {
            Prepare();
            return Page();
        }

        if (tag is null)
        {
            tag = new Tag { OwnerId = owner };
            db.Tags.Add(tag);
        }

        tag.Name = Name;
        tag.Color = Color.Length == 0 ? null : Color.ToLowerInvariant();
        await db.SaveChangesAsync();
        Flash($"Tag “{tag.Name}” is saved.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == Id && t.OwnerId == me.RequiredId);
        if (tag is null)
        {
            return NotFound();
        }

        // the tag leaves the notes; the notes stay
        await db.NoteTags.Where(nt => nt.TagId == tag.Id).ExecuteDeleteAsync();
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        Flash($"Tag “{tag.Name}” is deleted.");
        return RedirectToPage("Index");
    }

    private void Prepare()
    {
        ViewData["Title"] = IsNew ? "New tag" : "Edit tag";
        Crumbs(("Tags", "/Tags"), (IsNew ? "New tag" : Name, null));
    }
}

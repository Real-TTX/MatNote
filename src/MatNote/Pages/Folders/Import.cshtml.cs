using MatNote.Controls;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Folders;

/// <summary>
/// Imports a zip into the folders of the signed-in person: a folder that was exported as a MatNotes archive (with types, tags, favorites and links), or any zip of files
/// (directories become folders, files become notes). Always their own, always as new notes: nothing that is there is changed.
/// </summary>
public sealed class ImportModel(CurrentUser me, FolderService folders, NoteService notes, FolderImporter importer) : AppPageModel
{
    private const long MaxZipBytes = 600L * 1024 * 1024;

    /// <summary>The folder the import goes into (its id); empty for the top level.</summary>
    [BindProperty(SupportsGet = true)] public string Into { get; set; } = string.Empty;

    [BindProperty] public IFormFile? Archive { get; set; }

    public List<Choice> FolderChoices { get; private set; } = [];

    public ImportResult? Result { get; private set; }

    public long MaxEntryMb => notes.MaxFileBytes / (1024 * 1024);

    public async Task<IActionResult> OnGetAsync()
    {
        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var owner = me.RequiredId;
        long? target = null;
        if (Into.Length > 0)
        {
            var refs = await folders.RefsAsync(owner);
            if (!long.TryParse(Into, out var parsed) || refs.All(r => r.Id != parsed))
            {
                ModelState.AddModelError(nameof(Into), "Please choose one of your folders.");
            }
            else
            {
                target = parsed;
            }
        }

        if (Archive is null || Archive.Length == 0)
        {
            ModelState.AddModelError(nameof(Archive), "Please choose a zip file.");
        }
        else if (Archive.Length > MaxZipBytes)
        {
            ModelState.AddModelError(nameof(Archive), "The zip is too big (600 MB at most).");
        }

        ImportPlan? plan = null;
        if (ModelState.IsValid)
        {
            try
            {
                await using var stream = Archive!.OpenReadStream();
                plan = FolderArchive.Read(stream, notes.MaxFileBytes, Path.GetFileNameWithoutExtension(Archive.FileName).Replace(".matnotes", string.Empty, StringComparison.OrdinalIgnoreCase));
            }
            catch (InvalidDataException ex)
            {
                ModelState.AddModelError(nameof(Archive), ex.Message.Length > 0 && !ex.Message.StartsWith("End of Central", StringComparison.Ordinal) ? ex.Message : "This is not a zip file.");
            }
        }

        if (plan is not null && plan.Folders.Count == 0 && plan.Notes.Count == 0)
        {
            ModelState.AddModelError(nameof(Archive), "There is nothing in this zip that can be imported.");
        }

        if (!ModelState.IsValid || plan is null)
        {
            await PrepareAsync();
            return Page();
        }

        Result = await importer.ImportAsync(plan, owner, target);
        await PrepareAsync();
        return Page();
    }

    private async Task PrepareAsync()
    {
        FolderChoices = await folders.ChoicesAsync(me.RequiredId);
        ViewData["Title"] = "Import a folder";
        ViewData["ActiveNav"] = "folders";
        Crumbs(("Folders", "/Folders"), ("Import", null));
    }
}

using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Notes;

/// <summary>
/// Create or change a note: a page of its own (no editing in a dialog). A note has a type that is chosen once: rich text, plain text, markdown, or a file
/// (Word, Excel, PDF or any other). Someone who may only edit a shared note changes its title and text, nothing else.
/// </summary>
public sealed class EditModel(AppDbContext db, CurrentUser me, AccessService access, NoteService notes, FolderService folders, Clock clock, AttachmentService attachments) : AppPageModel
{
    public static readonly Choice[] Kinds =
    [
        new("RichText", "Rich text", "Formatting, lists, links, links to other notes"),
        new("PlainText", "Plain text", "Exactly what you type: prompts, snippets, logs"),
        new("Markdown", "Markdown", "Plain text with simple markup"),
        new("File", "Upload a file", "Word, Excel, PDF or any other file"),
    ];

    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Title { get; set; } = string.Empty;

    /// <summary>For a new note: RichText, PlainText, Markdown or File (the type of a file comes from its name).</summary>
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "RichText";

    [BindProperty] public new string Content { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)] public string Folder { get; set; } = string.Empty;

    [BindProperty] public string Tags { get; set; } = string.Empty;

    [BindProperty] public bool Favorite { get; set; }

    [BindProperty] public IFormFile? Upload { get; set; }

    /// <summary>When the note was last saved, as the page last saw it (microseconds of the update date): autosave refuses to overwrite a newer text. The JSON answer carries it as text: it is larger than a script number can hold exactly.</summary>
    [BindProperty] public long Stamp { get; set; }

    public bool IsNew => Id is null;

    private static long StampOf(Note note) => StampOf(note.UpdateDate);

    private static long StampOf(DateTime updateDate) => updateDate.Ticks / 10;

    public Access Access { get; private set; } = Access.Owner;

    public Note? Existing { get; private set; }

    public NoteType Type { get; private set; } = NoteType.RichText;

    public List<Choice> FolderChoices { get; private set; } = [];

    public IReadOnlyList<AttachmentView> Attachments { get; private set; } = [];

    public List<Choice> TagChoices { get; private set; } = [];

    public bool IsOwner => Access == Access.Owner;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var (note, level) = await access.FindNoteAsync(id, Access.Edit);
            if (note is null)
            {
                return NotFound();
            }

            Existing = note;
            Stamp = StampOf(note);
            Access = level;
            Type = note.Type;
            Title = note.Title;
            Content = note.Content;
            Folder = note.FolderId?.ToString() ?? string.Empty;
            Favorite = note.IsFavorite;
            if (level == Access.Owner)
            {
                Tags = string.Join(", ", await db.NoteTags.AsNoTracking().Where(nt => nt.NoteId == note.Id).Select(nt => nt.Tag!.Name).OrderBy(n => n).ToListAsync());
            }
        }
        else if (!await PrepareNewAsync())
        {
            return NotFound();
        }

        await PrepareAsync();
        return Page();
    }

    /// <summary>A new note: the folder it starts in (the person's own, or a shared one they may edit) and the kind.</summary>
    private async Task<bool> PrepareNewAsync()
    {
        Kind = Kinds.Any(k => k.Value == Kind) ? Kind : "RichText";
        if (Folder.Length > 0)
        {
            if (!long.TryParse(Folder, out var folderId))
            {
                return false;
            }

            var (folder, level) = await access.FindFolderAsync(folderId, Access.Edit);
            if (folder is null)
            {
                return false;
            }

            Access = level;
        }

        return true;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (failure, note) = await TrySaveAsync(autosave: false);
        if (failure is not null)
        {
            return failure;
        }

        Flash($"Note “{note!.Title}” is saved.");
        return RedirectToPage("View", new { id = note.Id, embed = EmbedValue });
    }

    /// <summary>
    /// The save while typing (autosave.js): the same checks as a save, the answer is JSON. A new note is created by the first one that has a title (text notes only);
    /// the answer carries its id and the new stamp, which the page sends back with the next one.
    /// </summary>
    public async Task<IActionResult> OnPostAutosaveAsync()
    {
        if (IsNew && (Kind == "File" || string.IsNullOrWhiteSpace(Title)))
        {
            return new JsonResult(new { ok = false, error = "Waiting for a title." }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
        }

        var (failure, note) = await TrySaveAsync(autosave: true);
        if (note is null)
        {
            return failure switch
            {
                ConflictResult => new JsonResult(new { ok = false, conflict = true, error = "This note was changed somewhere else." }) { StatusCode = StatusCodes.Status409Conflict },
                PageResult => new JsonResult(new { ok = false, error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "The note could not be saved." }) { StatusCode = StatusCodes.Status422UnprocessableEntity },
                _ => new JsonResult(new { ok = false, error = "The note was not found." }) { StatusCode = StatusCodes.Status404NotFound },
            };
        }

        var updated = await db.Notes.AsNoTracking().Where(n => n.Id == note.Id).Select(n => n.UpdateDate).FirstAsync();
        return new JsonResult(new { ok = true, id = note.Id, stamp = StampOf(updated).ToString(System.Globalization.CultureInfo.InvariantCulture), time = clock.ToLocal(DateTime.UtcNow).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) });
    }

    /// <summary>Checks and saves what was posted. A failure is the answer to give (not found, or the form again with its errors); otherwise the saved note.</summary>
    private async Task<(IActionResult? Failure, Note? Saved)> TrySaveAsync(bool autosave)
    {
        Note? note = null;
        long ownerId;
        if (Id is { } id)
        {
            var (found, level) = await access.FindNoteAsync(id, Access.Edit, track: true);
            if (found is null)
            {
                return (NotFound(), null);
            }

            if (autosave && Stamp > 0 && StampOf(found) != Stamp)
            {
                // somebody (or another window of this person) saved a newer text in the meantime: do not overwrite it behind their back
                return (new ConflictResult(), null);
            }

            note = found;
            Access = level;
            Type = note.Type;
            Existing = note;
            ownerId = note.OwnerId;
        }
        else
        {
            ownerId = me.RequiredId;
            if (Folder.Length > 0)
            {
                // a new note in a folder that was shared with edit rights belongs to the owner of the folder
                if (!long.TryParse(Folder, out var folderId))
                {
                    return (NotFound(), null);
                }

                var (folder, level) = await access.FindFolderAsync(folderId, Access.Edit);
                if (folder is null)
                {
                    return (NotFound(), null);
                }

                Access = level;
                ownerId = folder.OwnerId;
            }
        }

        Title = Title.Trim();
        if (Title.Length == 0)
        {
            ModelState.AddModelError(nameof(Title), "Please give the note a title.");
        }
        else if (Title.Length > NoteService.MaxTitleLength)
        {
            ModelState.AddModelError(nameof(Title), $"The title is too long ({NoteService.MaxTitleLength} characters at most).");
        }

        var isFile = IsNew ? Kind == "File" : !Ui.IsText(Type);
        if (IsNew && !isFile)
        {
            Type = Kind switch { "PlainText" => NoteType.PlainText, "Markdown" => NoteType.Markdown, _ => NoteType.RichText };
        }

        if (!isFile && Content.Length > NoteService.MaxTextLength)
        {
            ModelState.AddModelError(nameof(Content), "The text is too long.");
        }

        if (Upload is not null)
        {
            if (notes.CheckFile(Upload) is { } problem)
            {
                ModelState.AddModelError(nameof(Upload), problem);
            }
            else if (!IsNew && isFile && NoteService.TypeFor(Upload.FileName).IsTextImport)
            {
                ModelState.AddModelError(nameof(Upload), "Choose a Word, Excel, PDF or other file here. A text file is added as a new text note.");
            }
        }
        else if (IsNew && isFile)
        {
            ModelState.AddModelError(nameof(Upload), "Please choose a file.");
        }

        long? folderId2 = null;
        if (IsOwner && Folder.Length > 0)
        {
            var own = await folders.RefsAsync(ownerId);
            if (!long.TryParse(Folder, out var parsed) || own.All(r => r.Id != parsed))
            {
                ModelState.AddModelError(nameof(Folder), "Please choose one of your folders.");
            }
            else
            {
                folderId2 = parsed;
            }
        }
        else if (!IsOwner && IsNew && long.TryParse(Folder, out var shared))
        {
            folderId2 = shared;
        }

        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return (Page(), null);
        }

        if (note is null)
        {
            note = new Note { OwnerId = ownerId, Type = isFile ? NoteType.File : Type, CreateUserId = me.RequiredId };
        }

        note.Title = Title;
        if (IsOwner)
        {
            note.FolderId = folderId2;
            note.IsFavorite = Favorite;
        }
        else if (IsNew)
        {
            note.FolderId = folderId2;
        }

        if (!isFile)
        {
            note.Content = Content;
        }

        await notes.SaveAsync(note, Upload, IsOwner ? Tags : null);
        return (null, note);
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var (note, level) = await access.FindNoteAsync(Id ?? 0, Access.Owner, track: true);
        if (note is null || level != Access.Owner)
        {
            return NotFound();
        }

        await notes.DeleteAsync(note);
        Flash($"Note “{note.Title}” is in the trash.");
        if (Embed)
        {
            return RedirectToPage("Pane", new { embed = 1, gone = true });
        }

        return note.FolderId is { } folder ? RedirectToPage("Index", new { folder }) : RedirectToPage("Index");
    }

    private async Task PrepareAsync()
    {
        var owner = Existing?.OwnerId ?? me.RequiredId;
        if (Id is { } existingId)
        {
            Attachments = await attachments.ListAsync(existingId);
        }

        if (IsOwner)
        {
            FolderChoices = await folders.ChoicesAsync(owner);
            TagChoices = (await db.Tags.AsNoTracking().Where(t => t.OwnerId == owner).OrderBy(t => t.Name).Select(t => t.Name).ToListAsync()).Select(n => new Choice(n, n)).ToList();
        }

        ViewData["Title"] = IsNew ? "New note" : "Edit note";
        if (Existing is not null)
        {
            ViewData["NoteId"] = Existing.Id;
        }

        ViewData["ActiveNav"] = IsNew ? "new" : IsOwner ? "all" : "shares";
        if (Existing?.FolderId is { } folderId && IsOwner)
        {
            ViewData["ActiveFolder"] = folderId;
        }

        Crumbs(("Notes", "/Notes"), (IsNew ? "New note" : Title.Length > 0 ? Title : "Edit note", null));
    }
}

using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Notes;

public sealed record HistoryRow(VersionRow Version, string Who, string What);

/// <summary>
/// The history of a text note: the earlier states MatNotes kept (see <see cref="VersionPolicy"/>: not at every autosave), what changed since one of them, a version saved on purpose, and putting an
/// old state back. For someone who may edit the note. Who saved a state is shown as "You", the owner by name, and for anybody else as "Another editor": editors do not learn about each other.
/// </summary>
public sealed class HistoryModel(CurrentUser me, AccessService access, NoteVersionService versions, NoteService notes, INotePreview preview, AppDbContext db) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    /// <summary>The version that is compared with the text now.</summary>
    [BindProperty(SupportsGet = true)] public long? Version { get; set; }

    /// <summary>The name of a version saved on purpose.</summary>
    [BindProperty] public string Label { get; set; } = string.Empty;

    public Note Note { get; private set; } = null!;

    /// <summary>The signed-in person owns the note (only the owner shares it).</summary>
    public bool IsOwnerView => Note.OwnerId == me.RequiredId;

    public List<HistoryRow> Rows { get; private set; } = [];

    public string CurrentWho { get; private set; } = string.Empty;

    public HistoryRow? Selected { get; private set; }

    public NoteVersion? SelectedVersion { get; private set; }

    /// <summary>The changes since the selected version, long stretches without a change left out; <c>null</c> when the texts are too big to compare.</summary>
    public List<DiffLine>? Changes { get; private set; }

    public bool NoChanges { get; private set; }

    public string? TitleBefore { get; private set; }

    /// <summary>The selected version as it looked (safe HTML).</summary>
    public string? PreviewHtml { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync(track: false))
        {
            return NotFound();
        }

        await FillAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCheckpointAsync()
    {
        if (!await LoadAsync(track: false))
        {
            return NotFound();
        }

        await versions.CheckpointAsync(Note, Label);
        Flash("The version is saved.");
        return RedirectToPage(new { id = Id, embed = EmbedValue });
    }

    public async Task<IActionResult> OnPostRestoreAsync(long version)
    {
        if (!await LoadAsync(track: true))
        {
            return NotFound();
        }

        var old = await versions.GetAsync(Id, version);
        if (old is null)
        {
            return NotFound();
        }

        if (await versions.PrepareRestoreAsync(Note, old))
        {
            await notes.SaveAsync(Note, null, null);
            Flash("The old version is back. What was there before is kept in the history.");
        }
        else
        {
            Flash("That is the text of the note already.");
        }

        return RedirectToPage(new { id = Id, embed = EmbedValue });
    }

    /// <summary>The note, for someone who may edit it; a text note only (a file has no history).</summary>
    private async Task<bool> LoadAsync(bool track)
    {
        var (note, _) = await access.FindNoteAsync(Id, Access.Edit, track);
        if (note is null || !Ui.IsText(note.Type))
        {
            return false;
        }

        Note = note;
        ViewData["NoteId"] = note.Id;
        return true;
    }

    private async Task FillAsync()
    {
        ViewData["Title"] = "History of " + Note.Title;
        ViewData["ActiveNav"] = Note.OwnerId == me.RequiredId ? "all" : "shares";
        Crumbs(("Notes", "/Notes"), (Note.Title, $"/Notes/View?id={Note.Id}"), ("History", null));
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == Note.OwnerId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? string.Empty;
        var names = new Dictionary<long, string>();
        if (Note.OwnerId == me.RequiredId)
        {
            // the owner knows everybody who may edit (they named them)
            var authors = (await versions.ListAsync(Note.Id)).Select(v => v.AuthorId).Append(Note.UpdateUserId).OfType<long>().Distinct().ToList();
            names = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        }

        string Who(long? author) => author is null ? "Unknown"
            : author == me.RequiredId ? "You"
            : author == Note.OwnerId ? owner
            : names.TryGetValue(author.Value, out var name) ? name : "Another editor";

        CurrentWho = Who(Note.UpdateUserId ?? Note.OwnerId);
        Rows = (await versions.ListAsync(Note.Id)).Select(v => new HistoryRow(v, Who(v.AuthorId), v.Kind switch
        {
            VersionKind.Manual => string.IsNullOrWhiteSpace(v.Label) ? "Saved on purpose" : v.Label!,
            VersionKind.BeforeRestore => "Before an old version was put back",
            _ => "Kept before a change",
        })).ToList();

        if (Version is not { } picked || Rows.FirstOrDefault(r => r.Version.Id == picked) is not { } row)
        {
            return;
        }

        Selected = row;
        SelectedVersion = await versions.GetAsync(Note.Id, picked);
        if (SelectedVersion is null)
        {
            return;
        }

        PreviewHtml = preview.RenderText(new Note { Type = Note.Type, Content = SelectedVersion.Content }, noteId => $"/Notes/View?id={noteId}{EmbedQuery}");
        TitleBefore = SelectedVersion.Title != Note.Title ? SelectedVersion.Title : null;
        var all = TextDiff.Lines(VersionText.Of(Note.Type, SelectedVersion.Content), VersionText.Of(Note.Type, Note.Content));
        NoChanges = all is not null && all.All(l => l.Op == ' ') && TitleBefore is null;
        Changes = all is null ? null : TextDiff.Collapse(all);
    }
}

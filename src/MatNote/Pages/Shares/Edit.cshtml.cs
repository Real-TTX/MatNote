using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Shares;

public sealed record ShareListItem(long Id, ShareKind Kind, string? WithUser, string? WithName, SharePermission Permission, DateTime? Expires, bool HasPassword);

/// <summary>
/// Share a note or a folder (everything inside it) with one user or with anyone who has the link, or change or stop an existing share. Only the owner can.
/// A user is found by the exact user name: there is no list of users to choose from (nobody sees who else uses the app).
/// </summary>
public sealed class EditModel(AppDbContext db, CurrentUser me, AccessService access, ShareService shares, Clock clock) : AppPageModel
{
    public static readonly Choice[] Kinds = [new("Link", "Anyone with the link", "A web address; no account needed; read only"), new("User", "One user of MatNotes", "By user name; read, or read and edit")];

    public static readonly Choice[] Permissions = [new("Read", "Read", "See, copy and export"), new("Edit", "Read and edit", "Also change the title and the text")];

    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty(SupportsGet = true)] public long? NoteId { get; set; }

    [BindProperty(SupportsGet = true)] public long? FolderId { get; set; }

    [BindProperty] public string Kind { get; set; } = "Link";

    [BindProperty] public string UserName { get; set; } = string.Empty;

    [BindProperty] public string Permission { get; set; } = "Read";

    /// <summary>The last day the share works (a date), empty for no end.</summary>
    [BindProperty] public string Until { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public bool RemovePassword { get; set; }

    [BindProperty] public bool AllowExport { get; set; } = true;

    [BindProperty] public string Label { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public string ItemName { get; private set; } = string.Empty;

    public string ItemKind { get; private set; } = string.Empty;

    public string ItemIcon { get; private set; } = "file-text";

    public string BackUrl { get; private set; } = "/Shares/Mine";

    public string? LinkUrl { get; private set; }

    public bool HasPassword { get; private set; }

    public bool KindIsFixed => !IsNew;

    public List<ShareListItem> Others { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var (found, share) = await LoadTargetAsync(track: false);
        if (!found)
        {
            return NotFound();
        }

        if (share is not null)
        {
            Kind = share.Kind.ToString();
            Permission = share.Permission.ToString();
            AllowExport = share.AllowExport;
            Label = share.Label ?? string.Empty;
            HasPassword = share.PasswordHash is not null;
            Until = share.ExpiresDate is { } end ? clock.ToLocal(end.AddSeconds(-1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            if (share.SharedWithUserId is { } uid)
            {
                UserName = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.Username).FirstOrDefaultAsync() ?? string.Empty;
            }

            if (share.Kind == ShareKind.Link)
            {
                LinkUrl = shares.LinkUrl(Request, share);
            }
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (found, share) = await LoadTargetAsync(track: true);
        if (!found)
        {
            return NotFound();
        }

        var kind = IsNew ? (Kind == "User" ? ShareKind.User : ShareKind.Link) : share!.Kind;
        User? target = null;
        if (kind == ShareKind.User && IsNew)
        {
            var name = LoginService.Normalize(UserName);
            if (name.Length == 0)
            {
                ModelState.AddModelError(nameof(UserName), "Please enter the user name of the person.");
            }
            else if (!ShareService.AllowLookup(me.RequiredId))
            {
                ModelState.AddModelError(nameof(UserName), "Too many tries. Wait a few minutes before you try another name.");
            }
            else
            {
                target = await db.Users.FirstOrDefaultAsync(u => u.Username == name && u.IsActive);
                if (target is null)
                {
                    ModelState.AddModelError(nameof(UserName), "There is no user with this name. Ask the person for their exact user name.");
                }
                else if (target.Id == me.RequiredId)
                {
                    ModelState.AddModelError(nameof(UserName), "That is you: it is your own note.");
                }
            }
        }

        DateTime? expires = null;
        if (Until.Length > 0)
        {
            if (!DateTime.TryParseExact(Until, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
            {
                ModelState.AddModelError(nameof(Until), "Please choose a date.");
            }
            else if (day.Date < clock.ToLocal(DateTime.UtcNow).Date)
            {
                ModelState.AddModelError(nameof(Until), "The last day cannot be in the past.");
            }
            else
            {
                expires = clock.ToUtc(day.Date.AddDays(1));
            }
        }

        if (kind == ShareKind.Link && Password.Length > 0 && Password.Length < 4)
        {
            ModelState.AddModelError(nameof(Password), "Use at least 4 characters, or leave it empty for no password.");
        }

        if (!ModelState.IsValid)
        {
            HasPassword = share?.PasswordHash is not null;
            if (share is { Kind: ShareKind.Link })
            {
                LinkUrl = shares.LinkUrl(Request, share);
            }

            await PrepareAsync();
            return Page();
        }

        if (share is null)
        {
            // the same note or folder for the same person is one share: it is changed, not doubled
            share = kind == ShareKind.User
                ? await db.Shares.FirstOrDefaultAsync(s => s.Kind == ShareKind.User && s.SharedWithUserId == target!.Id && s.NoteId == NoteId && s.FolderId == FolderId)
                : null;
            if (share is null)
            {
                share = new Share { Kind = kind, OwnerId = me.RequiredId, NoteId = NoteId, FolderId = FolderId, SharedWithUserId = target?.Id };
                db.Shares.Add(share);
            }
        }

        share.Permission = kind == ShareKind.User && Permission == "Edit" ? SharePermission.Edit : SharePermission.Read;
        share.ExpiresDate = expires;
        share.Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();
        if (kind == ShareKind.Link)
        {
            share.AllowExport = AllowExport;
            if (RemovePassword)
            {
                share.PasswordHash = null;
            }

            if (Password.Length > 0)
            {
                share.PasswordHash = shares.HashPassword(Password);
            }
        }

        await db.SaveChangesAsync();
        if (kind == ShareKind.Link)
        {
            Flash(IsNew ? "The link is ready. Copy it and send it." : "The share is saved.");
            return RedirectToPage(new { id = share.Id, embed = EmbedValue });
        }

        Flash(IsNew ? $"Shared with {target!.DisplayName}." : "The share is saved.");
        return AfterChange(share);
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var share = await db.Shares.FirstOrDefaultAsync(s => s.Id == Id && s.OwnerId == me.RequiredId);
        if (share is null)
        {
            return NotFound();
        }

        db.Shares.Remove(share);
        await db.SaveChangesAsync();
        Flash("Sharing stopped. The link or the access no longer works.");
        return AfterChange(share);
    }

    /// <summary>After a user share or a stop: in the note pane back to the sharing tab of the note, otherwise the list of the shares.</summary>
    private IActionResult AfterChange(Share share) => Embed && share.NoteId is { } note ? RedirectToPage("Edit", new { noteId = note, embed = 1 }) : RedirectToPage("Mine");

    /// <summary>Finds what is shared (a new share: the note or folder from the address; an existing one: from the share). Only the owner may share.</summary>
    private async Task<(bool Found, Share? Share)> LoadTargetAsync(bool track)
    {
        Share? share = null;
        if (Id is { } id)
        {
            share = await (track ? db.Shares : db.Shares.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == me.RequiredId);
            if (share is null)
            {
                return (false, null);
            }

            NoteId = share.NoteId;
            FolderId = share.FolderId;
        }

        if (NoteId is { } noteId)
        {
            var (note, level) = await access.FindNoteAsync(noteId, Access.Owner);
            if (note is null || level != Access.Owner)
            {
                return (false, null);
            }

            ItemName = note.Title;
            ItemKind = Ui.TypeName(note.Type);
            ItemIcon = Ui.TypeIcon(note.Type);
            BackUrl = Embed ? (IsNew ? $"/Notes/View?id={note.Id}&embed=1" : $"/Shares/Edit?noteId={note.Id}&embed=1") : $"/Notes/View?id={note.Id}";
            ViewData["NoteId"] = note.Id;
            FolderId = null;
            return (true, share);
        }

        if (FolderId is { } folderId)
        {
            var (folder, level) = await access.FindFolderAsync(folderId, Access.Owner);
            if (folder is null || level != Access.Owner)
            {
                return (false, null);
            }

            ItemName = folder.Name;
            ItemKind = "Folder with everything inside it";
            ItemIcon = "folder";
            BackUrl = $"/Notes?folder={folder.Id}";
            return (true, share);
        }

        return (false, null);
    }

    private async Task PrepareAsync()
    {
        Others = await db.Shares.AsNoTracking()
            .Where(s => s.OwnerId == me.RequiredId && s.NoteId == NoteId && s.FolderId == FolderId && s.Id != Id)
            .OrderByDescending(s => s.CreateDate)
            .Select(s => new ShareListItem(s.Id, s.Kind, s.SharedWithUser != null ? s.SharedWithUser.Username : null, s.SharedWithUser != null ? s.SharedWithUser.DisplayName : null, s.Permission, s.ExpiresDate, s.PasswordHash != null))
            .ToListAsync();
        ViewData["Title"] = IsNew ? "Share" : "Edit share";
        ViewData["ActiveNav"] = "shares";
        Crumbs(("Shares", "/Shares/Mine"), (IsNew ? "Share " + ItemName : ItemName, null));
    }
}

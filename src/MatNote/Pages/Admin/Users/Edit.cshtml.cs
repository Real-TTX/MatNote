using MatNote.Controls;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Admin.Users;

/// <summary>Create or change an account, reset its two-step verification, end its sessions, delete it. Delete is here, not in the list.</summary>
public sealed class EditModel(AppDbContext db, CurrentUser me, UserService users, PasswordService passwords, SessionService sessions) : AppPageModel
{
    public static readonly Choice[] Roles = [new("User", "User", "Their own notes, folders and tags; shares"), new("Admin", "Administrator", "Also: users, backups, system. Still no access to the notes of others")];

    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string Role { get; set; } = "User";

    [BindProperty] public bool IsActive { get; set; } = true;

    /// <summary>On an existing account: choose a new password.</summary>
    [BindProperty] public bool SetPassword { get; set; }

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public bool MustChange { get; set; } = true;

    public bool IsNew => Id is null;

    public bool IsMe => Id == me.Id;

    public bool Totp { get; private set; }

    public int NoteCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return NotFound();
            }

            DisplayName = user.DisplayName;
            Username = user.Username;
            Email = user.Email;
            Role = user.Role.ToString();
            IsActive = user.IsActive;
            Totp = user.TotpEnabled;
            NoteCount = await db.Notes.CountAsync(n => n.OwnerId == id);
        }

        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        User? user = null;
        if (Id is { } id)
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return NotFound();
            }
        }

        await users.ValidateIdentityAsync(ModelState, Username, DisplayName, Email, Id);
        var role = Role == "Admin" ? UserRole.Admin : UserRole.User;
        if (IsNew || SetPassword)
        {
            UserService.ValidatePassword(ModelState, Password, Password, LoginService.Normalize(Username));
        }

        if (user is not null && IsMe && (role != UserRole.Admin || !IsActive))
        {
            ModelState.AddModelError(nameof(Role), "You cannot take the administrator role from yourself or disable your own account.");
        }
        else if (user is not null && user.Role == UserRole.Admin && (role != UserRole.Admin || !IsActive) && !await OtherAdminExistsAsync(user.Id))
        {
            ModelState.AddModelError(nameof(Role), "This is the last active administrator.");
        }

        if (!ModelState.IsValid)
        {
            await RefillAsync(user);
            return Page();
        }

        if (user is null)
        {
            user = users.Create(Username, DisplayName, Email, Password, role, MustChange);
            user.IsActive = IsActive;
        }
        else
        {
            user.Username = LoginService.Normalize(Username);
            user.DisplayName = DisplayName.Trim();
            user.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim().ToLowerInvariant();
            user.Role = role;
            user.IsActive = IsActive;
            if (SetPassword)
            {
                user.PasswordHash = passwords.Hash(Password);
                user.MustChangePassword = MustChange;
                user.FailedLogins = 0;
                user.LockoutUntil = null;
            }

            if (!IsActive || SetPassword)
            {
                await sessions.RevokeAllAsync(user.Id);
            }
        }

        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash($"Account “{user.DisplayName}” is saved.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostResetTotpAsync()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        user.TotpEnabled = false;
        user.TotpSecret = null;
        user.RecoveryCodes = null;
        user.TotpLastStep = 0;
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash($"Two-step verification of “{user.DisplayName}” is off. They can set it up again.");
        return RedirectToPage(new { id = user.Id });
    }

    public async Task<IActionResult> OnPostUnlockAsync()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        user.FailedLogins = 0;
        user.LockoutUntil = null;
        await db.SaveChangesAsync();
        Flash($"“{user.DisplayName}” can sign in again.");
        return RedirectToPage(new { id = user.Id });
    }

    public async Task<IActionResult> OnPostSignOutAsync()
    {
        if (Id is not { } id || !await db.Users.AnyAsync(u => u.Id == id))
        {
            return NotFound();
        }

        await sessions.RevokeAllAsync(id, IsMe ? HttpContext.Items["MatNote.SessionId"] as long? : null);
        Flash("Every device of this account was signed out.");
        return RedirectToPage(new { id });
    }

    /// <summary>Deletes the account with everything that belongs to it: notes, folders, tags and shares go to the trash state and are removed for good by the housekeeping.</summary>
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == me.Id)
        {
            FlashError("You cannot delete your own account.");
            return RedirectToPage(new { id = user.Id });
        }

        if (user.Role == UserRole.Admin && !await OtherAdminExistsAsync(user.Id))
        {
            FlashError("This is the last active administrator.");
            return RedirectToPage(new { id = user.Id });
        }

        await sessions.RevokeAllAsync(user.Id);
        foreach (var note in await db.Notes.Where(n => n.OwnerId == user.Id).ToListAsync())
        {
            db.Notes.Remove(note);
        }

        foreach (var folder in await db.Folders.Where(f => f.OwnerId == user.Id).ToListAsync())
        {
            db.Folders.Remove(folder);
        }

        foreach (var tag in await db.Tags.Where(t => t.OwnerId == user.Id).ToListAsync())
        {
            db.Tags.Remove(tag);
        }

        foreach (var share in await db.Shares.Where(s => s.OwnerId == user.Id || s.SharedWithUserId == user.Id).ToListAsync())
        {
            db.Shares.Remove(share);
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash($"Account “{user.DisplayName}” is deleted.");
        return RedirectToPage("Index");
    }

    private Task<bool> OtherAdminExistsAsync(long exceptId) => db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != exceptId);

    private async Task RefillAsync(User? user)
    {
        if (user is not null)
        {
            Totp = user.TotpEnabled;
            NoteCount = await db.Notes.CountAsync(n => n.OwnerId == user.Id);
        }

        Prepare();
    }

    private void Prepare()
    {
        ViewData["Title"] = IsNew ? "New user" : "Edit user";
        Crumbs(("Administration", null), ("Users", "/Admin/Users"), (IsNew ? "New user" : DisplayName, null));
    }
}

using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Account;

/// <summary>The account: name and e-mail, the password and the look of the app (the theme and the colour scheme are kept per user).</summary>
public sealed class IndexModel(AppDbContext db, CurrentUser current, UserService users, PasswordService passwords, SessionService sessions) : AppPageModel
{
    public static readonly string[] Themes = ["system", "light", "dark"];
    public static readonly string[] Schemes = ["standard", "paper", "contrast", "oled"];

    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty] public string NewPassword { get; set; } = string.Empty;

    [BindProperty] public string Repeat { get; set; } = string.Empty;

    public bool MustChange => current.Snapshot?.MustChangePassword == true;

    public string Scheme => current.Snapshot?.Scheme ?? "standard";

    public async Task OnGetAsync()
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == current.RequiredId);
        DisplayName = user.DisplayName;
        Username = user.Username;
        Email = user.Email;
        Crumbs(("Account", null));
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequiredId);
        Username = user.Username;
        await users.ValidateIdentityAsync(ModelState, user.Username, DisplayName, Email, user.Id);
        if (!ModelState.IsValid)
        {
            Crumbs(("Account", null));
            return Page();
        }

        user.DisplayName = DisplayName.Trim();
        user.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim().ToLowerInvariant();
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash("Your account is saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequiredId);
        Username = user.Username;
        DisplayName = user.DisplayName;
        Email = user.Email;
        if (!passwords.Verify(user.PasswordHash, CurrentPassword))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "The current password is not right.");
        }

        UserService.ValidatePassword(ModelState, NewPassword, Repeat, user.Username, nameof(NewPassword), nameof(Repeat));
        if (!ModelState.IsValid)
        {
            Crumbs(("Account", null));
            return Page();
        }

        user.PasswordHash = passwords.Hash(NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        // every other browser has to sign in again with the new password
        var sessionId = HttpContext.Items["MatNote.SessionId"] as long?;
        await sessions.RevokeAllAsync(user.Id, sessionId);
        Flash("Your password is changed. Other devices were signed out.");
        return RedirectToPage();
    }

    /// <summary>The theme button in the menu posts here (<c>data-theme-save-url</c>).</summary>
    public async Task<IActionResult> OnPostThemeAsync(string theme)
    {
        if (Themes.Contains(theme))
        {
            await db.Users.Where(u => u.Id == current.RequiredId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Theme, theme));
            SessionService.Invalidate();
        }

        return new StatusCodeResult(204);
    }

    /// <summary>The colour scheme control posts here (<c>data-scheme-save-url</c>).</summary>
    public async Task<IActionResult> OnPostSchemeAsync(string scheme)
    {
        if (Schemes.Contains(scheme))
        {
            await db.Users.Where(u => u.Id == current.RequiredId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Scheme, scheme));
            SessionService.Invalidate();
        }

        return new StatusCodeResult(204);
    }
}

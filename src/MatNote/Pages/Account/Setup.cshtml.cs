using System.ComponentModel.DataAnnotations;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Account;

/// <summary>The setup assistant: on a fresh database the first visitor creates the administrator. After that the page is gone.</summary>
public sealed class SetupModel(AppDbContext db, UserService users, SetupState state, SessionService sessions) : AppPageModel
{
    [BindProperty, StringLength(120)] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public string Repeat { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
        => await state.NeedsSetupAsync(db) ? Page() : Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await state.NeedsSetupAsync(db))
        {
            return Redirect("/");
        }

        await users.ValidateIdentityAsync(ModelState, Username, DisplayName, Email);
        UserService.ValidatePassword(ModelState, Password, Repeat, Username);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var admin = users.Create(Username, DisplayName, Email, Password, UserRole.Admin);
        admin.LastLoginDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        state.Done();
        await sessions.SignInAsync(HttpContext, admin, remember: true);
        Flash("Welcome to MatNotes. You are the administrator.");
        return Redirect("/");
    }
}

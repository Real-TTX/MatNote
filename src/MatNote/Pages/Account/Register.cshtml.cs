using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Account;

/// <summary>Creating an account yourself. Only there when the administrator allowed it on the System page.</summary>
public sealed class RegisterModel(AppDbContext db, UserService users, SessionService sessions, ConfigStore<AppConfig> config) : AppPageModel
{
    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public string Repeat { get; set; } = string.Empty;

    public IActionResult OnGet() => config.Value.AllowRegistration ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!config.Value.AllowRegistration)
        {
            return NotFound();
        }

        await users.ValidateIdentityAsync(ModelState, Username, DisplayName, Email);
        UserService.ValidatePassword(ModelState, Password, Repeat, Username);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = users.Create(Username, DisplayName, Email, Password, UserRole.User);
        user.LastLoginDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await sessions.SignInAsync(HttpContext, user, remember: true);
        Flash("Your account is ready.");
        return Redirect("/");
    }
}

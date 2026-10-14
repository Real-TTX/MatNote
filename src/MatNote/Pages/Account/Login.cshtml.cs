using MatNote.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Account;

public sealed class LoginModel(LoginService login, ConfigStore<AppConfig> config, MatNote.Data.AppDbContext db, SetupState setup, CurrentUser current) : AppPageModel
{
    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public bool Remember { get; set; } = true;

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public bool CanRegister => config.Value.AllowRegistration;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await setup.NeedsSetupAsync(db))
        {
            return Redirect("/Account/Setup");
        }

        return current.IsAuthenticated ? RedirectLocal(ReturnUrl) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (outcome, user) = await login.CheckPasswordAsync(Username, Password, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (outcome == LoginOutcome.Failed || user is null)
        {
            // the same words for a wrong name, a wrong password, a locked and a disabled account: nothing tells a guesser which one it was
            ModelState.AddModelError(string.Empty, "The user name or the password is not right, or the account is locked for a while.");
            Password = string.Empty;
            return Page();
        }

        if (outcome == LoginOutcome.NeedsCode)
        {
            login.SetPending(HttpContext, user, Remember);
            return RedirectToPage("Verify", new { ReturnUrl });
        }

        await login.CompleteAsync(HttpContext, user, Remember);
        return RedirectLocal(ReturnUrl);
    }
}

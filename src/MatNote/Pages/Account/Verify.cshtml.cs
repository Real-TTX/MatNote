using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Account;

/// <summary>The second step of the sign-in: the code of the authenticator app, or a recovery code.</summary>
public sealed class VerifyModel(LoginService login) : AppPageModel
{
    [BindProperty] public string Code { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)] public bool Recovery { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (user, _) = await login.ReadPendingAsync(HttpContext);
        return user is null ? RedirectToPage("Login", new { ReturnUrl }) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (user, remember) = await login.ReadPendingAsync(HttpContext);
        if (user is null)
        {
            return RedirectToPage("Login", new { ReturnUrl });
        }

        if (!await login.CheckCodeAsync(user, Code, HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            ModelState.AddModelError(nameof(Code), Recovery ? "That recovery code is not valid." : "That code is not valid or has expired. Check the app and try again.");
            Code = string.Empty;
            return Page();
        }

        LoginService.ClearPending(HttpContext);
        await login.CompleteAsync(HttpContext, user, remember);
        return RedirectLocal(ReturnUrl);
    }
}

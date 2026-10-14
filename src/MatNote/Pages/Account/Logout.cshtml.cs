using MatNote.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Account;

public sealed class LogoutModel(SessionService sessions) : AppPageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await sessions.SignOutAsync(HttpContext);
        return RedirectToPage("Login");
    }
}

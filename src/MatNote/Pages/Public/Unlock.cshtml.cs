using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Public;

/// <summary>The password of a link. A wrong password counts against the address, like a wrong password at the sign-in.</summary>
public sealed class UnlockModel(PublicShareAccess pub, ShareService shares) : PageModel
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime Since)> Failures = new();

    [FromRoute] public string Token { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (status, _) = await pub.ResolveAsync(HttpContext, Token);
        return status switch
        {
            PublicShareAccess.Status.NotFound => NotFound(),
            PublicShareAccess.Status.Ok => Redirect("/s/" + Token),
            _ => Page(),
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (status, share) = await pub.ResolveAsync(HttpContext, Token);
        if (status == PublicShareAccess.Status.NotFound)
        {
            return NotFound();
        }

        if (status == PublicShareAccess.Status.Ok)
        {
            return Redirect("/s/" + Token);
        }

        var key = $"{HttpContext.Connection.RemoteIpAddress}:{share!.Id}";
        var now = DateTime.UtcNow;
        if (Failures.TryGetValue(key, out var f) && now - f.Since < TimeSpan.FromMinutes(10) && f.Count >= 10)
        {
            Error = "Too many tries. Wait a few minutes and try again.";
            return Page();
        }

        if (!shares.CheckPassword(share, Password))
        {
            Failures.AddOrUpdate(key, _ => (1, now), (_, old) => now - old.Since > TimeSpan.FromMinutes(10) ? (1, now) : (old.Count + 1, old.Since));
            Error = "That password is not right.";
            return Page();
        }

        Failures.TryRemove(key, out _);
        pub.Unlock(HttpContext, share);
        return Redirect("/s/" + Token);
    }
}

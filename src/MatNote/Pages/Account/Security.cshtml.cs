using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Pages.Account;

public sealed record SessionRow(long Id, string Device, string? Address, DateTime LastSeen, bool IsCurrent);

/// <summary>Two-step verification (TOTP with an authenticator app, recovery codes) and the devices that are signed in.</summary>
public sealed class SecurityModel(AppDbContext db, CurrentUser current, TotpService totp, PasswordService passwords, SessionService sessions, SecretProtector secrets) : AppPageModel
{
    public const string Issuer = "MatNotes";

    [BindProperty] public string Code { get; set; } = string.Empty;

    [BindProperty] public string CurrentPassword { get; set; } = string.Empty;

    public bool Enabled { get; private set; }

    /// <summary>A secret was made but not confirmed with a code yet.</summary>
    public bool Pending { get; private set; }

    public string? Secret { get; private set; }

    public string? QrSvg { get; private set; }

    public int CodesLeft { get; private set; }

    /// <summary>The recovery codes, shown once right after they were made.</summary>
    public IReadOnlyList<string>? NewCodes { get; private set; }

    public List<SessionRow> Sessions { get; private set; } = [];

    private async Task<User> LoadUserAsync() => await db.Users.FirstAsync(u => u.Id == current.RequiredId);

    public async Task OnGetAsync() => await FillAsync(await LoadUserAsync());

    private async Task FillAsync(User user, IReadOnlyList<string>? newCodes = null)
    {
        Crumbs(("Account", "/Account"), ("Security", null));
        Enabled = user.TotpEnabled;
        Pending = !user.TotpEnabled && !string.IsNullOrEmpty(user.TotpSecret);
        CodesLeft = TotpService.RecoveryCodesLeft(user);
        NewCodes = newCodes;
        if (Pending && secrets.Unprotect(user.TotpSecret) is { } secret)
        {
            Secret = string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4).Select(i => secret.Substring(i * 4, Math.Min(4, secret.Length - i * 4))));
            QrSvg = TotpService.QrSvg(TotpService.ProvisioningUri(Issuer, user.Username, secret));
        }

        var currentId = HttpContext.Items["MatNote.SessionId"] as long?;
        var now = DateTime.UtcNow;
        var rows = await db.Sessions.AsNoTracking().Where(s => s.UserId == user.Id && !s.IsRevoked && s.ExpiresDate > now).OrderByDescending(s => s.LastSeenDate).ToListAsync();
        Sessions = rows.Select(s => new SessionRow(s.Id, Describe(s.UserAgent), s.IpAddress, s.LastSeenDate, s.Id == currentId)).ToList();
    }

    /// <summary>Step 1: make a secret. It is not in effect until a code from the app has been typed (step 2).</summary>
    public async Task<IActionResult> OnPostStartAsync()
    {
        var user = await LoadUserAsync();
        if (!user.TotpEnabled)
        {
            user.TotpSecret = totp.Protect(TotpService.NewSecret());
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        var user = await LoadUserAsync();
        if (!user.TotpEnabled)
        {
            user.TotpSecret = null;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    /// <summary>Step 2: the code of the app proves that the secret is in it; from now on the sign-in asks for a code.</summary>
    public async Task<IActionResult> OnPostConfirmAsync()
    {
        var user = await LoadUserAsync();
        if (user.TotpEnabled || string.IsNullOrEmpty(user.TotpSecret))
        {
            return RedirectToPage();
        }

        if (!totp.Verify(user, Code))
        {
            ModelState.AddModelError(nameof(Code), "That code is not valid. Check the app and try again; the codes change every 30 seconds.");
            await FillAsync(user);
            return Page();
        }

        var (codes, stored) = TotpService.NewRecoveryCodes();
        user.TotpEnabled = true;
        user.RecoveryCodes = stored;
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        await FillAsync(user, codes);
        return Page();
    }

    public async Task<IActionResult> OnPostNewCodesAsync()
    {
        var user = await LoadUserAsync();
        if (!user.TotpEnabled || !passwords.Verify(user.PasswordHash, CurrentPassword))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "The password is not right.");
            await FillAsync(user);
            return Page();
        }

        var (codes, stored) = TotpService.NewRecoveryCodes();
        user.RecoveryCodes = stored;
        await db.SaveChangesAsync();
        await FillAsync(user, codes);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await LoadUserAsync();
        if (!passwords.Verify(user.PasswordHash, CurrentPassword))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "The password is not right.");
        }
        else if (!totp.Verify(user, Code) && !TotpService.UseRecoveryCode(user, Code))
        {
            ModelState.AddModelError(nameof(Code), "That code is not valid.");
        }

        if (!ModelState.IsValid)
        {
            await FillAsync(user);
            return Page();
        }

        user.TotpEnabled = false;
        user.TotpSecret = null;
        user.RecoveryCodes = null;
        user.TotpLastStep = 0;
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash("Two-step verification is off.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(long id)
    {
        await sessions.RevokeAsync(id, current.RequiredId);
        Flash("The device was signed out.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeOthersAsync()
    {
        await sessions.RevokeAllAsync(current.RequiredId, HttpContext.Items["MatNote.SessionId"] as long?);
        Flash("All other devices were signed out.");
        return RedirectToPage();
    }

    /// <summary>"Chrome on Windows" from the user agent: enough to tell the devices apart.</summary>
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return "Unknown device";
        }

        var browser = userAgent.Contains("Edg/") ? "Edge" : userAgent.Contains("OPR/") ? "Opera" : userAgent.Contains("Firefox/") ? "Firefox"
            : userAgent.Contains("Chrome/") ? "Chrome" : userAgent.Contains("Safari/") ? "Safari" : "Browser";
        var system = userAgent.Contains("Android") ? "Android" : userAgent.Contains("iPhone") || userAgent.Contains("iPad") ? "iOS"
            : userAgent.Contains("Windows") ? "Windows" : userAgent.Contains("Mac OS") ? "macOS" : userAgent.Contains("Linux") ? "Linux" : "an unknown system";
        return $"{browser} on {system}";
    }
}

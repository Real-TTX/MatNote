using System.Collections.Concurrent;
using MatNote.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

public enum LoginOutcome
{
    Success,
    NeedsCode,
    Failed,
}

/// <summary>The sign-in: the password, the lock after repeated failures, and the second step (the code of an authenticator app, or a recovery code).</summary>
public sealed class LoginService(AppDbContext db, PasswordService passwords, TotpService totp, SessionService sessions, IDataProtectionProvider provider)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);
    public const string PendingCookie = "matnote.2fa";
    private static readonly TimeSpan PendingTime = TimeSpan.FromMinutes(5);

    // failures per address (a guess of names from one place is slowed down, whichever names it tries)
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> Addresses = new();
    private const int MaxFailuresPerAddress = 30;
    private static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _pending = provider.CreateProtector("MatNote.Pending2fa.v1").ToTimeLimitedDataProtector();

    public static string Normalize(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Checks name and password. With two-step verification on, the person is not signed in yet: <see cref="LoginOutcome.NeedsCode"/>.</summary>
    public async Task<(LoginOutcome Outcome, User? User)> CheckPasswordAsync(string? username, string? password, string? address)
    {
        password ??= string.Empty;
        if (AddressBlocked(address))
        {
            passwords.SpendTime(password);
            return (LoginOutcome.Failed, null);
        }

        var name = Normalize(username);
        var user = name.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.Username == name || (u.Email != null && u.Email.ToLower() == name));
        if (user is null)
        {
            passwords.SpendTime(password);
            CountAddress(address);
            return (LoginOutcome.Failed, null);
        }

        var now = DateTime.UtcNow;
        if (user.LockoutUntil > now || !user.IsActive)
        {
            passwords.SpendTime(password);
            return (LoginOutcome.Failed, null);
        }

        if (!passwords.Verify(user.PasswordHash, password))
        {
            await CountFailureAsync(user);
            CountAddress(address);
            return (LoginOutcome.Failed, null);
        }

        return (user.TotpEnabled ? LoginOutcome.NeedsCode : LoginOutcome.Success, user);
    }

    /// <summary>The second step: a code of the app, or a recovery code. A wrong code counts like a wrong password.</summary>
    public async Task<bool> CheckCodeAsync(User user, string? code, string? address)
    {
        code = (code ?? string.Empty).Trim();
        var now = DateTime.UtcNow;
        if (user.LockoutUntil > now || AddressBlocked(address))
        {
            return false;
        }

        var ok = code.Contains('-') || code.Any(char.IsLetter)
            ? TotpService.UseRecoveryCode(user, code)
            : totp.Verify(user, code);
        if (!ok)
        {
            await CountFailureAsync(user);
            CountAddress(address);
            return false;
        }

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>The person is who they say: create the session. Resets the failures.</summary>
    public async Task CompleteAsync(HttpContext http, User user, bool remember)
    {
        user.FailedLogins = 0;
        user.LockoutUntil = null;
        user.LastLoginDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await sessions.SignInAsync(http, user, remember);
    }

    // ---- the pending second step: a short-lived, signed cookie that says "the password was right for user N" ----

    public void SetPending(HttpContext http, User user, bool remember)
    {
        var value = _pending.Protect($"{user.Id}|{(remember ? 1 : 0)}", PendingTime);
        http.Response.Cookies.Append(PendingCookie, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Secure = http.Request.IsHttps,
            MaxAge = PendingTime,
        });
    }

    public async Task<(User? User, bool Remember)> ReadPendingAsync(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(PendingCookie, out var value))
        {
            return (null, false);
        }

        try
        {
            var parts = _pending.Unprotect(value).Split('|');
            var id = long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            return (await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive), parts[1] == "1");
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or IndexOutOfRangeException)
        {
            return (null, false);
        }
    }

    public static void ClearPending(HttpContext http) => http.Response.Cookies.Delete(PendingCookie);

    private async Task CountFailureAsync(User user)
    {
        user.FailedLogins++;
        if (user.FailedLogins >= MaxFailures)
        {
            user.FailedLogins = 0;
            user.LockoutUntil = DateTime.UtcNow + LockoutTime;
        }

        await db.SaveChangesAsync();
    }

    private static bool AddressBlocked(string? address)
        => address is not null && Addresses.TryGetValue(address, out var entry) && DateTime.UtcNow - entry.Since < AddressWindow && entry.Count >= MaxFailuresPerAddress;

    private static void CountAddress(string? address)
    {
        if (address is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        Addresses.AddOrUpdate(address, _ => (1, now), (_, old) => now - old.Since > AddressWindow ? (1, now) : (old.Count + 1, old.Since));
    }
}

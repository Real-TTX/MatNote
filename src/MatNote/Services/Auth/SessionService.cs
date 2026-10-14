using System.Security.Claims;
using MatNote.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MatNote.Services;

/// <summary>
/// Sessions. A session is a row in <c>UserSession</c>; the cookie only carries its token (signed and encrypted by the data protection keys, which are in the volume),
/// so a session survives a restart of the container and an administrator can end it. Every request checks the session (cached for a few seconds).
/// </summary>
public sealed class SessionService(AppDbContext db, ConfigStore<AppConfig> config, IMemoryCache cache)
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string SessionClaim = "sid";

    private static readonly TimeSpan ShortSession = TimeSpan.FromHours(12);
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);
    private static int _generation;

    private sealed record Cached(UserSnapshot User, long SessionId, DateTime ExpiresDate, DateTime LastSeenDate, bool Remember);

    /// <summary>Forget what was cached about sessions and users (after a user or a session changed): every session is checked again on its next request.</summary>
    public static void Invalidate() => Interlocked.Increment(ref _generation);

    public static UserSnapshot Snapshot(User u) => new(u.Id, u.Username, u.DisplayName, u.Role, u.Theme, u.Scheme, u.MustChangePassword, u.TotpEnabled);

    /// <summary>Creates the session and the cookie of a person who has proven who they are.</summary>
    public async Task SignInAsync(HttpContext http, User user, bool remember)
    {
        var now = DateTime.UtcNow;
        var lifetime = remember ? TimeSpan.FromDays(Math.Clamp(config.Value.SessionDays, 1, 365)) : ShortSession;
        var session = new UserSession
        {
            UserId = user.Id,
            Token = Guid.NewGuid(),
            LastSeenDate = now,
            ExpiresDate = now + lifetime,
            CreateUserId = user.Id,
            UserAgent = Trim(http.Request.Headers.UserAgent.ToString(), 300),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
            [new Claim(SessionClaim, session.Token.ToString()), new Claim("rem", remember ? "1" : "0")],
            Scheme);
        await http.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = remember,
            ExpiresUtc = session.ExpiresDate,
            AllowRefresh = true,
        });
    }

    /// <summary>Ends the session of this browser.</summary>
    public async Task SignOutAsync(HttpContext http)
    {
        if (Guid.TryParse(http.User.FindFirstValue(SessionClaim), out var token))
        {
            await db.Sessions.Where(s => s.Token == token).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
            Invalidate();
        }

        await http.SignOutAsync(Scheme);
    }

    public async Task RevokeAsync(long sessionId, long userId)
    {
        await db.Sessions.Where(s => s.Id == sessionId && s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
        Invalidate();
    }

    /// <summary>Ends every session of a user, except one (the current one, after a change of the password).</summary>
    public async Task RevokeAllAsync(long userId, long? exceptSessionId = null)
    {
        await db.Sessions.Where(s => s.UserId == userId && s.Id != exceptSessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
        Invalidate();
    }

    /// <summary>Runs for every request with a cookie: accepts it only while its session is valid and the user is active, and gives the request the user.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var http = context.HttpContext;
        var token = context.Principal?.FindFirstValue(SessionClaim);
        var service = http.RequestServices.GetRequiredService<SessionService>();
        var found = Guid.TryParse(token, out var guid) ? await service.LoadAsync(guid) : null;
        if (found is null)
        {
            context.RejectPrincipal();
            await http.SignOutAsync(Scheme);
            return;
        }

        http.Items[CurrentUser.ItemKey] = found.User;
        http.Items["MatNote.SessionId"] = found.SessionId;

        // the role comes from the database on every request: a change of the role or a disabled account works at once
        var claims = new List<Claim>
        {
            new(SessionClaim, guid.ToString()),
            new("rem", found.Remember ? "1" : "0"),
            new(ClaimTypes.NameIdentifier, found.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, found.User.Username),
            new(ClaimTypes.Role, found.User.Role.ToString()),
        };
        context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)));
    }

    private async Task<Cached?> LoadAsync(Guid token)
    {
        var key = $"session:{_generation}:{token}";
        if (!cache.TryGetValue(key, out Cached? cached))
        {
            var row = await db.Sessions.AsNoTracking()
                .Where(s => s.Token == token)
                .Select(s => new { s.Id, s.IsRevoked, s.ExpiresDate, s.LastSeenDate, s.User })
                .FirstOrDefaultAsync();
            if (row is null || row.IsRevoked || row.User is null || !row.User.IsActive)
            {
                return null;
            }

            var remember = row.ExpiresDate - row.LastSeenDate > ShortSession + TimeSpan.FromMinutes(1);
            cached = new Cached(Snapshot(row.User), row.Id, row.ExpiresDate, row.LastSeenDate, remember);
            cache.Set(key, cached, TimeSpan.FromSeconds(20));
        }

        if (cached is null || cached.ExpiresDate < DateTime.UtcNow)
        {
            return null;
        }

        return await TouchAsync(cached, key);
    }

    /// <summary>A session that is used slides: its expiry moves on, but the database is written only every few minutes.</summary>
    private async Task<Cached> TouchAsync(Cached cached, string key)
    {
        var now = DateTime.UtcNow;
        if (now - cached.LastSeenDate < TouchEvery)
        {
            return cached;
        }

        var lifetime = cached.Remember ? TimeSpan.FromDays(Math.Clamp(config.Value.SessionDays, 1, 365)) : ShortSession;
        await db.Sessions.Where(s => s.Id == cached.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenDate, now).SetProperty(x => x.ExpiresDate, now + lifetime));
        var touched = cached with { LastSeenDate = now, ExpiresDate = now + lifetime };
        cache.Set(key, touched, TimeSpan.FromSeconds(20));
        return touched;
    }

    private static string? Trim(string? text, int max) => string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
}

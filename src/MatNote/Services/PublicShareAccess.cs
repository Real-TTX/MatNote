using MatNote.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>
/// The anonymous side: a person with a link (no account) reads what was shared, nothing else. A link can have a password; after the right one a short-lived,
/// signed cookie for this link says so. Every note or folder asked for through a link must be inside what that link shares.
/// </summary>
public sealed class PublicShareAccess(ShareService shares, AppDbContext db, IDataProtectionProvider provider)
{
    private static readonly TimeSpan UnlockTime = TimeSpan.FromHours(8);
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("MatNote.ShareUnlock.v1").ToTimeLimitedDataProtector();

    public enum Status
    {
        Ok,
        NotFound,
        NeedsPassword,
    }

    public static string CookieName(Guid token) => ShareService.UnlockCookiePrefix + token.ToString("N")[..12];

    public async Task<(Status Status, Share? Share)> ResolveAsync(HttpContext http, string token)
    {
        var share = await shares.FindLinkAsync(token);
        if (share is null)
        {
            return (Status.NotFound, null);
        }

        if (share.PasswordHash is not null && !IsUnlocked(http, share))
        {
            return (Status.NeedsPassword, share);
        }

        return (Status.Ok, share);
    }

    public bool IsUnlocked(HttpContext http, Share share)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName(share.Token), out var value))
        {
            return false;
        }

        try
        {
            // the cookie names the hash of the password too, so a changed password ends every unlock
            return _protector.Unprotect(value) == Mark(share);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    public void Unlock(HttpContext http, Share share)
        => http.Response.Cookies.Append(CookieName(share.Token), _protector.Protect(Mark(share), UnlockTime), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Secure = http.Request.IsHttps,
            Path = "/s",
            MaxAge = UnlockTime,
        });

    private static string Mark(Share share) => $"{share.Id}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(share.PasswordHash ?? string.Empty)))[..16]}";

    /// <summary>The note, when it is inside the share.</summary>
    public async Task<Note?> NoteAsync(Share share, long noteId)
    {
        var note = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.OwnerId == share.OwnerId);
        return note is not null && await AccessService.LinkCoversAsync(db, share, note) ? note : null;
    }

    /// <summary>The folder, when it is the shared folder or inside it.</summary>
    public async Task<Folder?> FolderAsync(Share share, long folderId)
    {
        if (share.FolderId is not { } root)
        {
            return null;
        }

        var all = await db.Folders.AsNoTracking().Where(f => f.OwnerId == share.OwnerId).Select(f => new FolderRef(f.Id, f.ParentId)).ToListAsync();
        return FolderTree.SelfAndDescendants(all, root).Contains(folderId) ? await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId) : null;
    }

    /// <summary>Counts a visit of the link (the number the owner sees).</summary>
    public async Task CountViewAsync(Share share)
    {
        var now = DateTime.UtcNow;
        await db.Shares.Where(s => s.Id == share.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1).SetProperty(x => x.LastAccessDate, now));
    }
}

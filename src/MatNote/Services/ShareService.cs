using System.Collections.Concurrent;
using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>Shares: the address of a link, finding a valid link, the password of a link, and the limit on guessing user names.</summary>
public sealed class ShareService(AppDbContext db, PasswordService passwords, ConfigStore<AppConfig> config)
{
    public const string UnlockCookiePrefix = "matnote.share.";

    // How often one person may try a user name in the share form: a name is the only thing that tells a user from a stranger, so guessing them is slowed down.
    private static readonly ConcurrentDictionary<long, (int Count, DateTime Since)> Lookups = new();
    private const int MaxLookups = 20;
    private static readonly TimeSpan LookupWindow = TimeSpan.FromMinutes(10);

    /// <summary>The address of a link share, built on the public address of the app (System page) or the address of the request.</summary>
    public string LinkUrl(HttpRequest request, Share share)
    {
        var configured = config.Value.PublicBaseUrl.Trim().TrimEnd('/');
        var root = configured.Length > 0 ? configured : $"{request.Scheme}://{request.Host}";
        return $"{root}/s/{share.Token:N}";
    }

    public static bool IsExpired(Share share) => share.ExpiresDate is { } end && end <= DateTime.UtcNow;

    /// <summary>The link share with this token while it is valid: not ended, not expired, and the note or folder it points to still exists.</summary>
    public async Task<Share?> FindLinkAsync(string token)
    {
        if (!Guid.TryParse(token, out var guid))
        {
            return null;
        }

        var share = await db.Shares.AsNoTracking().FirstOrDefaultAsync(s => s.Token == guid && s.Kind == ShareKind.Link);
        if (share is null || IsExpired(share))
        {
            return null;
        }

        var exists = share.NoteId is { } noteId
            ? await db.Notes.AnyAsync(n => n.Id == noteId)
            : share.FolderId is { } folderId && await db.Folders.AnyAsync(f => f.Id == folderId);
        return exists ? share : null;
    }

    public bool CheckPassword(Share share, string? password) => share.PasswordHash is null || passwords.Verify(share.PasswordHash, password ?? string.Empty);

    public string HashPassword(string password) => passwords.Hash(password);

    /// <summary>Counts a try of a user name for this person; <c>false</c> when they have tried too often.</summary>
    public static bool AllowLookup(long userId)
    {
        var now = DateTime.UtcNow;
        var entry = Lookups.AddOrUpdate(userId, _ => (1, now), (_, old) => now - old.Since > LookupWindow ? (1, now) : (old.Count + 1, old.Since));
        return entry.Count <= MaxLookups;
    }

    /// <summary>The name of what is shared, with its kind: for the lists.</summary>
    public static string TargetName(Share share) => share.Note?.Title ?? share.Folder?.Name ?? "(deleted)";
}

using MatNote.Data;

namespace MatNote.Services;

/// <summary>What the pages need to know about the signed-in person, read once per request from the session (no query of its own).</summary>
public sealed record UserSnapshot(long Id, string Username, string DisplayName, UserRole Role, string Theme, string Scheme, bool MustChangePassword, bool TotpEnabled);

/// <summary>The signed-in user of the request. Background work has none (<see cref="Id"/> is <c>null</c>).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public const string ItemKey = "MatNote.User";

    public UserSnapshot? Snapshot => accessor.HttpContext?.Items[ItemKey] as UserSnapshot;

    public bool IsAuthenticated => Snapshot is not null;

    public long? Id => Snapshot?.Id;

    /// <summary>The id of the signed-in person; throws when there is none (only call it behind the sign-in).</summary>
    public long RequiredId => Snapshot?.Id ?? throw new InvalidOperationException("No signed-in user.");

    public bool IsAdmin => Snapshot?.Role == UserRole.Admin;

    public string DisplayName => Snapshot?.DisplayName ?? string.Empty;

    public string Username => Snapshot?.Username ?? string.Empty;

    public string RoleName => Snapshot?.Role == UserRole.Admin ? "Administrator" : "User";
}

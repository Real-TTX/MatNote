namespace MatNote.Data;

/// <summary>What every record has: an Id (BIGINT) and who created and changed it, and when (the columns are filled by <see cref="AppDbContext"/>).</summary>
public abstract class Entity
{
    public long Id { get; set; }

    public DateTime CreateDate { get; set; }

    public long? CreateUserId { get; set; }

    public DateTime UpdateDate { get; set; }

    public long? UpdateUserId { get; set; }
}

/// <summary>A record that supports soft delete: 0 = deleted, 1 = created, 2 = updated. A deleted record is hidden by a query filter (notes stay in the trash).</summary>
public abstract class SoftEntity : Entity
{
    public const short Deleted = 0;
    public const short Created = 1;
    public const short Updated = 2;

    public short UpdateState { get; set; } = Created;
}

public enum UserRole
{
    Admin = 1,
    User = 2,
}

public enum NoteType
{
    /// <summary>Rich text: HTML made by the editor, sanitized.</summary>
    RichText = 0,

    PlainText = 1,

    Markdown = 2,

    /// <summary>A Word document (the file is kept as it is).</summary>
    Docx = 3,

    /// <summary>An Excel workbook (the file is kept as it is).</summary>
    Xlsx = 4,

    Pdf = 5,

    /// <summary>Any other file.</summary>
    File = 6,
}

public enum SharePermission
{
    Read = 0,
    Edit = 1,
}

public enum ShareKind
{
    /// <summary>With one user of the app.</summary>
    User = 0,

    /// <summary>With anyone who has the link (the anonymous role).</summary>
    Link = 1,
}

public sealed class User : SoftEntity
{
    /// <summary>The name to sign in with; unique, compared without regard to case.</summary>
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    /// <summary>Set when an administrator chose the password: the person has to choose their own after the first sign-in.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>The secret of the authenticator app, protected with the data protection keys (never plain text).</summary>
    public string? TotpSecret { get; set; }

    public bool TotpEnabled { get; set; }

    /// <summary>The time step of the last accepted code: a code is valid once.</summary>
    public long TotpLastStep { get; set; }

    /// <summary>The hashes of the recovery codes that are not used yet, as a JSON array.</summary>
    public string? RecoveryCodes { get; set; }

    public int FailedLogins { get; set; }

    public DateTime? LockoutUntil { get; set; }

    public DateTime? LastLoginDate { get; set; }

    /// <summary>system, light or dark.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>standard, paper, contrast or oled.</summary>
    public string Scheme { get; set; } = "standard";
}

/// <summary>A signed-in browser. The cookie carries the <see cref="Token"/>; the session lives in the database, so it survives a restart of the container.</summary>
public sealed class UserSession : Entity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime LastSeenDate { get; set; }

    public DateTime ExpiresDate { get; set; }

    public bool IsRevoked { get; set; }

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }
}

public sealed class Folder : SoftEntity
{
    public long OwnerId { get; set; }

    public User? Owner { get; set; }

    public long? ParentId { get; set; }

    public Folder? Parent { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>A colour the user chose, <c>#rrggbb</c>.</summary>
    public string? Color { get; set; }
}

public sealed class Note : SoftEntity
{
    public long OwnerId { get; set; }

    public User? Owner { get; set; }

    public long? FolderId { get; set; }

    public Folder? Folder { get; set; }

    public string Title { get; set; } = string.Empty;

    public NoteType Type { get; set; } = NoteType.RichText;

    /// <summary>The HTML of a rich text note, the markdown, or the plain text. Empty for a file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The text of the note without markup (or extracted from the file): what the search looks in.</summary>
    public string SearchText { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    public string? FileName { get; set; }

    public string? ContentType { get; set; }

    public long FileSize { get; set; }

    public List<NoteTag> Tags { get; set; } = [];
}

/// <summary>The bytes of a file note; a table of its own so that a list of notes never loads them.</summary>
public sealed class NoteFile : Entity
{
    public long NoteId { get; set; }

    public Note? Note { get; set; }

    public byte[] Data { get; set; } = [];

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class Tag : SoftEntity
{
    public long OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }
}

public sealed class NoteTag : Entity
{
    public long NoteId { get; set; }

    public Note? Note { get; set; }

    public long TagId { get; set; }

    public Tag? Tag { get; set; }
}

/// <summary>A note that links to another note (the link is in its content). Kept by the app on every save: it gives the backlinks.</summary>
public sealed class NoteLink : Entity
{
    public long SourceNoteId { get; set; }

    public long TargetNoteId { get; set; }
}

/// <summary>A share of a note or a folder (with everything inside it), with one user or with anyone who has the link.</summary>
public sealed class Share : SoftEntity
{
    /// <summary>The secret in the address of a link share.</summary>
    public Guid Token { get; set; } = Guid.NewGuid();

    public ShareKind Kind { get; set; }

    public long OwnerId { get; set; }

    public long? NoteId { get; set; }

    public Note? Note { get; set; }

    public long? FolderId { get; set; }

    public Folder? Folder { get; set; }

    public long? SharedWithUserId { get; set; }

    public User? SharedWithUser { get; set; }

    public SharePermission Permission { get; set; }

    /// <summary>A link can have a password (a salted hash).</summary>
    public string? PasswordHash { get; set; }

    public DateTime? ExpiresDate { get; set; }

    /// <summary>Whether a visitor of the link can also download and export.</summary>
    public bool AllowExport { get; set; } = true;

    public string? Label { get; set; }

    public int ViewCount { get; set; }

    public DateTime? LastAccessDate { get; set; }
}

public enum BackupStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>One run of a backup (the history; the backups themselves are the files at the target).</summary>
public sealed class BackupRun : Entity
{
    public string ScheduleId { get; set; } = string.Empty;

    public string ScheduleName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    /// <summary>manual or scheduled.</summary>
    public string Trigger { get; set; } = "manual";

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public BackupStatus Status { get; set; }

    public string? Message { get; set; }

    public string? FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>How many old backups the retention removed after this run.</summary>
    public int Removed { get; set; }
}

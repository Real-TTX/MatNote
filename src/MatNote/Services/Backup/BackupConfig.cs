using System.Text.Json.Serialization;

namespace MatNote.Services.Backup;

public enum BackupTargetType
{
    /// <summary>A directory of the container (a mounted volume, for example a NAS share mounted on the host).</summary>
    Folder,

    /// <summary>A share on a NAS or a server, reached over SMB 2/3 without a mount.</summary>
    Smb,
}

public enum BackupFrequency
{
    Hourly,
    Daily,
    Weekly,
    Monthly,

    /// <summary>A cron expression (five fields) for everything the friendly choices cannot say.</summary>
    Cron,
}

/// <summary>
/// The backup configuration (<c>config/backup.json</c>): where the backups go and when they are made. The backups themselves are files at the targets;
/// the history of the runs is in the database (<c>BackupRun</c>).
/// </summary>
public sealed class BackupConfig
{
    public List<BackupTarget> Targets { get; set; } = [];

    public List<BackupSchedule> Schedules { get; set; } = [];
}

/// <summary>Where backups are written to, read from and deleted.</summary>
public sealed class BackupTarget
{
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = string.Empty;

    public BackupTargetType Type { get; set; } = BackupTargetType.Folder;

    /// <summary>Folder: the directory (the default <c>/backups</c> is the mount point of the Docker image).</summary>
    public string Path { get; set; } = "/backups";

    /// <summary>SMB: the name or the address of the server.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 445;

    public string Share { get; set; } = string.Empty;

    /// <summary>SMB: a folder inside the share (<c>matnotes/backups</c>); empty: the root of the share.</summary>
    public string SubFolder { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    /// <summary>The password, protected with the data protection keys (<see cref="SecretProtector"/>). It is never shown on a page; an empty field on the edit page keeps it.</summary>
    public string Password { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(Password);

    /// <summary>A short, readable description of where the target points (no secrets).</summary>
    [JsonIgnore]
    public string Location => Type == BackupTargetType.Folder
        ? Path
        : $@"\\{Host}{(Port == 445 ? string.Empty : ":" + Port)}\{Share}{(string.IsNullOrEmpty(SubFolder) ? string.Empty : "\\" + SubFolder.Replace('/', '\\').Trim('\\'))}";

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

/// <summary>A backup is kept when ANY rule keeps it; 0 turns a rule off, and all four at 0 keep everything.</summary>
public sealed class BackupRetention
{
    /// <summary>The newest N backups.</summary>
    public int KeepLast { get; set; } = 7;

    /// <summary>The newest backup of each of the last N days that have one.</summary>
    public int KeepDaily { get; set; } = 7;

    /// <summary>The newest backup of each of the last N (ISO) weeks that have one.</summary>
    public int KeepWeekly { get; set; } = 4;

    /// <summary>The newest backup of each of the last N months that have one.</summary>
    public int KeepMonthly { get; set; } = 6;

    [JsonIgnore]
    public bool KeepsEverything => KeepLast <= 0 && KeepDaily <= 0 && KeepWeekly <= 0 && KeepMonthly <= 0;
}

/// <summary>When a backup is made, where it goes and how long the backups of this schedule are kept.</summary>
public sealed class BackupSchedule
{
    public string Id { get; set; } = BackupTarget.NewId();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string TargetId { get; set; } = string.Empty;

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    /// <summary>Daily, weekly, monthly: the time of day (<c>HH:mm</c>, in the time zone of the app).</summary>
    public string Time { get; set; } = "02:30";

    /// <summary>Hourly: the minute of the hour.</summary>
    public int Minute { get; set; }

    /// <summary>Hourly: every N hours (a divisor of 24, so that the rhythm is the same every day).</summary>
    public int EveryHours { get; set; } = 1;

    /// <summary>Weekly: the day of the week.</summary>
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Monthly: the day of the month (1 to 28; 0 is the last day of the month).</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>Cron: the expression (minute hour day month weekday).</summary>
    public string Cron { get; set; } = "30 2 * * *";

    public BackupRetention Retention { get; set; } = new();

    /// <summary>Whether the archive contains the data protection keys. They are needed to read the TOTP secrets and the passwords of the targets after a restore on a new host.</summary>
    public bool IncludeKeys { get; set; } = true;

    /// <summary>When the schedule was last changed (UTC). A slot that lay before it is not made up for.</summary>
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}

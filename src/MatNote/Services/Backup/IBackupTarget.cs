namespace MatNote.Services.Backup;

/// <summary>A backup file at a target: its name (see <see cref="BackupFileName"/>), its size and when it was written (UTC).</summary>
public sealed record BackupFile(string Name, long Size, DateTime ModifiedUtc);

/// <summary>A failure at a target (not reachable, wrong password, share missing, disk full ...) with a message an administrator can act on.</summary>
public sealed class BackupTargetException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// A place that holds backup files: a folder or a share on a NAS. Every method works only with file names that pass <see cref="BackupFileName.IsValid"/>
/// and throws <see cref="BackupTargetException"/> with a readable message when something goes wrong.
/// </summary>
public interface IBackupTarget
{
    /// <summary>The backup files in the target (other files are ignored), in no particular order.</summary>
    Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct);

    /// <summary>Writes the file. It appears under its final name only when it is complete (written as <c>.part</c> and renamed).</summary>
    Task UploadAsync(string name, Stream source, CancellationToken ct);

    Task DownloadAsync(string name, Stream destination, CancellationToken ct);

    Task DeleteAsync(string name, CancellationToken ct);

    /// <summary>Checks that the target can be reached and written (a small test file is written and removed again) and says what was found.</summary>
    Task<string> TestConnectionAsync(CancellationToken ct);
}

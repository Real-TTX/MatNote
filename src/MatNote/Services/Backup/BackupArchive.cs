using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services.Backup;

/// <summary>What <c>manifest.json</c> says about a backup: enough to know where it comes from and whether this version of the app can restore it.</summary>
public sealed class BackupManifest
{
    public int Format { get; set; } = 1;

    public string App { get; set; } = Ui.AppName;

    /// <summary>The version of the app that made the backup (<c>AppInfo.Version</c>).</summary>
    public string Version { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public string ScheduleId { get; set; } = string.Empty;

    public string ScheduleName { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    /// <summary>The last EF Core migration of the dump: the app can restore it when it knows this migration (and updates the tables if the dump is older).</summary>
    public string Migration { get; set; } = string.Empty;

    public bool IncludesKeys { get; set; }

    public List<string> ConfigFiles { get; set; } = [];

    /// <summary>How much of what was in the dump (a sanity check after a restore).</summary>
    public Dictionary<string, long> Counts { get; set; } = [];
}

/// <summary>
/// The zip of a backup: <c>manifest.json</c>, <c>database.dump</c> (<c>pg_dump --format=custom</c>), <c>config/*.json</c> and, when asked for, <c>keys/*</c>.
/// The keys are what reads the TOTP secrets and the passwords of the targets: an archive with them holds everything, so the place it is kept has to be protected like the server itself.
/// </summary>
public static partial class BackupArchive
{
    public const string ManifestEntry = "manifest.json";
    public const string DumpEntry = "database.dump";
    public const string ConfigPrefix = "config/";
    public const string KeysPrefix = "keys/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.json\z")]
    private static partial Regex ConfigName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
    private static partial Regex KeyName();

    /// <summary>A file name inside <c>config/</c> that may be restored: a plain name that ends in .json (no path, no leftovers like .tmp or .broken-...).</summary>
    public static bool IsConfigName(string name) => ConfigName().IsMatch(name) && name.Length <= 120;

    /// <summary>A file name inside <c>keys/</c> that may be restored: a plain name (a key ring file such as <c>key-3f2a....xml</c>).</summary>
    public static bool IsKeyName(string name) => KeyName().IsMatch(name) && name.Length <= 160 && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes the archive at <paramref name="zipPath"/>: the dump is made first (<c>pg_dump</c>), then everything is put in the zip.</summary>
    public static async Task BuildAsync(string zipPath, string scratchDirectory, BackupSchedule schedule, DateTime createdUtc, AppPaths paths, PgConnection connection, AppDbContext db, CancellationToken ct)
    {
        var dumpPath = Path.Combine(scratchDirectory, "dump-" + Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            var dump = await PgTools.DumpAsync(connection, dumpPath, ct);
            if (!dump.Ok)
            {
                throw new InvalidOperationException($"pg_dump failed (exit code {dump.ExitCode}): {PgTools.Tail(dump.Output)}");
            }

            var manifest = new BackupManifest
            {
                Version = AppInfo.Version,
                CreatedUtc = createdUtc,
                ScheduleId = schedule.Id,
                ScheduleName = schedule.Name,
                Database = connection.Database,
                Migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? string.Empty,
                IncludesKeys = schedule.IncludeKeys,
                Counts = await CountsAsync(db, ct),
            };

            var configFiles = ConfigFiles(paths);
            manifest.ConfigFiles = configFiles.Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();

            await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

            var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
            await using (var s = entry.Open())
            {
                await JsonSerializer.SerializeAsync(s, manifest, Json, ct);
            }

            // the dump is compressed already
            entry = zip.CreateEntry(DumpEntry, CompressionLevel.NoCompression);
            await using (var s = entry.Open())
            await using (var d = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await d.CopyToAsync(s, 81920, ct);
            }

            foreach (var path in configFiles)
            {
                await AddFileAsync(zip, ConfigPrefix + Path.GetFileName(path), path, ct);
            }

            if (schedule.IncludeKeys)
            {
                foreach (var path in Directory.EnumerateFiles(paths.Keys).Where(p => IsKeyName(Path.GetFileName(p))))
                {
                    await AddFileAsync(zip, KeysPrefix + Path.GetFileName(path), path, ct);
                }
            }
        }
        finally
        {
            TryDelete(dumpPath);
        }
    }

    /// <summary>The JSON files of <c>config</c>; leftovers (<c>*.tmp</c>, <c>*.broken-*</c>) are not part of a backup.</summary>
    public static List<string> ConfigFiles(AppPaths paths)
        => Directory.EnumerateFiles(paths.Config).Where(p => IsConfigName(Path.GetFileName(p))).OrderBy(p => p, StringComparer.Ordinal).ToList();

    private static async Task<Dictionary<string, long>> CountsAsync(AppDbContext db, CancellationToken ct) => new()
    {
        ["users"] = await db.Users.CountAsync(ct),
        ["folders"] = await db.Folders.CountAsync(ct),
        ["notes"] = await db.Notes.CountAsync(ct),
        ["files"] = await db.NoteFiles.CountAsync(ct),
        ["tags"] = await db.Tags.CountAsync(ct),
        ["shares"] = await db.Shares.CountAsync(ct),
    };

    private static async Task AddFileAsync(ZipArchive zip, string entryName, string path, CancellationToken ct)
    {
        // another part of the app may replace the file while it is read (a config file is written as .tmp and moved): read it whole, once
        byte[] bytes;
        try
        {
            await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var s = entry.Open();
        await s.WriteAsync(bytes, ct);
    }

    public static BackupManifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntry);
        if (entry is null || entry.Length > 1_000_000)
        {
            return null;
        }

        using var s = entry.Open();
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(s, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cleanup of the temp folder at the next start takes it
        }
    }
}

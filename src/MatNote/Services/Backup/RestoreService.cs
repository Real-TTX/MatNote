using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using MatNote.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MatNote.Services.Backup;

public sealed record RestoreOptions(string TargetId, string FileName, bool RestoreKeys, bool SafetyBackup);

/// <summary>What a restore did, for the result page: the steps (in order), the log of <c>pg_restore</c> and whether the app has to be restarted.</summary>
public sealed class RestoreResult
{
    public bool Succeeded { get; set; }

    /// <summary>The one sentence that says what happened.</summary>
    public string Summary { get; set; } = string.Empty;

    public List<string> Steps { get; } = [];

    public List<string> Warnings { get; } = [];

    public string Log { get; set; } = string.Empty;

    public bool NeedsRestart { get; set; }

    public BackupManifest? Manifest { get; set; }

    public string? SafetyFile { get; set; }
}

/// <summary>
/// Restores a backup into the running app: download, check the manifest, <c>pg_restore</c> in ONE transaction (a failed restore changes nothing), then the config files,
/// and the keys only when asked for. The checks come first and the database comes second, so nothing is half done when a file is wrong. The app keeps running.
/// </summary>
public sealed class RestoreService(
    IServiceScopeFactory scopes,
    ConfigStore<BackupConfig> config,
    ConfigStore<AppConfig> appConfig,
    BackupTargetFactory targets,
    BackupService backups,
    AppPaths paths,
    DatabaseInfo database,
    ILogger<RestoreService> logger)
{
    public async Task<RestoreResult> RestoreAsync(RestoreOptions options, CancellationToken ct)
    {
        var result = new RestoreResult();
        using var gate = backups.TryTakeGate("a restore");
        if (gate is null)
        {
            result.Summary = $"A backup is running right now ({backups.Current}). Try the restore again when it is done.";
            return result;
        }

        var zipPath = Path.Combine(paths.Temp, "restore-" + Guid.NewGuid().ToString("N") + ".zip");
        var dumpPath = Path.Combine(paths.Temp, "restore-" + Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            await RunAsync(options, result, zipPath, dumpPath, ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "The restore of {File} failed.", options.FileName);
            if (result.Succeeded)
            {
                // the database was restored already: say what went wrong afterwards, do not claim that nothing happened
                result.Warnings.Add($"A step after the database failed: {ex.Message}");
            }
            else
            {
                result.Summary = ex is OperationCanceledException ? "The restore was cancelled." : ex.Message;
            }
        }
        finally
        {
            TryDelete(zipPath);
            TryDelete(dumpPath);
        }

        return result;
    }

    private async Task RunAsync(RestoreOptions options, RestoreResult result, string zipPath, string dumpPath, CancellationToken ct)
    {
        if (!BackupFileName.IsValid(options.FileName))
        {
            throw new InvalidOperationException("This is not the name of a backup file.");
        }

        var target = config.Value.Targets.FirstOrDefault(t => t.Id == options.TargetId) ?? throw new InvalidOperationException("The target does not exist.");
        var source = targets.Create(target);

        // 1. what is checked before anything changes: the file, the manifest, the dump, the migration
        await using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.DownloadAsync(options.FileName, file, ct);
        }

        result.Steps.Add($"Downloaded {options.FileName} from “{target.Name}” ({Ui.Size(new FileInfo(zipPath).Length)}).");

        using var zip = OpenZip(zipPath);
        var manifest = BackupArchive.ReadManifest(zip) ?? throw new InvalidOperationException("The file has no readable manifest.json: it is not a backup of MatNotes, or it is damaged.");
        result.Manifest = manifest;
        if (manifest.Format != 1 || manifest.App != Ui.AppName)
        {
            throw new InvalidOperationException("The file is not a backup in the format this version understands.");
        }

        var dumpEntry = zip.GetEntry(BackupArchive.DumpEntry) ?? throw new InvalidOperationException("The file has no database dump: it is damaged.");
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (manifest.Migration.Length > 0 && !db.Database.GetMigrations().Contains(manifest.Migration))
            {
                throw new InvalidOperationException($"The backup was made by a newer version of the app ({manifest.Version}, database state {manifest.Migration}). Update the app first, then restore.");
            }
        }

        var configEntries = zip.Entries.Where(e => e.FullName.StartsWith(BackupArchive.ConfigPrefix, StringComparison.Ordinal) && e.Name.Length > 0 && e.FullName == BackupArchive.ConfigPrefix + e.Name && BackupArchive.IsConfigName(e.Name)).ToList();
        var keyEntries = options.RestoreKeys
            ? zip.Entries.Where(e => e.FullName.StartsWith(BackupArchive.KeysPrefix, StringComparison.Ordinal) && e.Name.Length > 0 && e.FullName == BackupArchive.KeysPrefix + e.Name && BackupArchive.IsKeyName(e.Name)).ToList()
            : [];
        if (options.RestoreKeys && keyEntries.Count == 0)
        {
            throw new InvalidOperationException("You asked for the encryption keys, but this backup has none (it was made without them). Nothing was changed.");
        }

        result.Steps.Add($"Checked the backup: made {manifest.CreatedUtc:yyyy-MM-dd HH:mm} UTC by version {manifest.Version}, {manifest.Counts.GetValueOrDefault("notes")} notes, {manifest.Counts.GetValueOrDefault("users")} users.");

        // 2. the safety net: the current data, to the same target
        if (options.SafetyBackup)
        {
            var safety = await backups.RunSafetyAsync(target, ct);
            if (!safety.Succeeded)
            {
                throw new InvalidOperationException($"The safety backup of the current data failed, so nothing was restored: {safety.Message}");
            }

            result.SafetyFile = safety.FileName;
            result.Steps.Add($"Made a safety backup of the current data: {safety.FileName}.");
        }

        // 3. the database, in one transaction
        await using (var entryStream = dumpEntry.Open())
        await using (var dumpFile = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await entryStream.CopyToAsync(dumpFile, 81920, ct);
        }

        var restore = await PgTools.RestoreAsync(PgConnection.Parse(database.ConnectionString), dumpPath, ct);
        result.Log = restore.Output;
        if (!restore.Ok)
        {
            throw new InvalidOperationException($"pg_restore failed (exit code {restore.ExitCode}). The restore runs in one transaction, so the database is unchanged. {PgTools.Tail(restore.Output, 4)}");
        }

        result.Steps.Add("Restored the database (pg_restore, one transaction).");
        NpgsqlConnection.ClearAllPools(); // the tables were replaced: connections start fresh

        // from here on the database is the backup's: what goes wrong now is reported as a warning, the restore itself is done
        result.Succeeded = true;
        result.Summary = "The backup was restored.";
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                await db.Database.MigrateAsync(ct);
                result.Steps.Add($"The backup is from an older version: updated the tables ({pending.Count} migration{(pending.Count == 1 ? string.Empty : "s")}).");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The restored database could not be brought up to date.");
            result.Warnings.Add($"The tables could not be updated to this version: {ex.Message} Restart the container; the update runs at the start.");
        }

        // 4. the configuration files
        var written = 0;
        foreach (var entry in configEntries)
        {
            try
            {
                await WriteAtomicAsync(Path.Combine(paths.Config, entry.Name), entry, ct);
                written++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"The configuration file {entry.Name} could not be written: {ex.Message}");
            }
        }

        ReloadStores(result);
        result.Steps.Add($"Restored {written} configuration {(written == 1 ? "file" : "files")}.");

        // 5. the keys, only when asked for
        if (options.RestoreKeys)
        {
            var keys = 0;
            foreach (var entry in keyEntries)
            {
                try
                {
                    await WriteAtomicAsync(Path.Combine(paths.Keys, entry.Name), entry, ct);
                    keys++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Warnings.Add($"The key file {entry.Name} could not be written: {ex.Message}");
                }
            }

            result.NeedsRestart = keys > 0;
            result.Steps.Add($"Restored {keys} encryption {(keys == 1 ? "key" : "keys")}. They are used after the container is restarted.");
        }

        SessionService.Invalidate();
        result.Steps.Add("Ended the cached sessions: every sign-in is checked against the restored data again.");
    }

    private static ZipArchive OpenZip(string path)
    {
        try
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("The file is not a zip archive, or it is damaged.", ex);
        }
    }

    private static async Task WriteAtomicAsync(string path, ZipArchiveEntry entry, CancellationToken ct)
    {
        var temp = path + ".restore-" + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        try
        {
            await using (var input = entry.Open())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await input.CopyToAsync(output, 81920, ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>The stores keep their value in memory: after new files were written they are told what is in them now (without a restart).</summary>
    private void ReloadStores(RestoreResult result)
    {
        Reload(config, "backup.json", result);
        Reload(appConfig, "app.json", result);
    }

    private void Reload<T>(ConfigStore<T> store, string file, RestoreResult result) where T : class, new()
    {
        var path = Path.Combine(paths.Config, file);
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var fresh = JsonSerializer.Deserialize<T>(File.ReadAllText(path), ConfigStore<T>.Json);
            if (fresh is null)
            {
                return;
            }

            store.Update(current =>
            {
                foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
                {
                    property.SetValue(current, property.GetValue(fresh));
                }
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            result.Warnings.Add($"{file} was restored, but the running app could not reload it: {ex.Message} Restart the container.");
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

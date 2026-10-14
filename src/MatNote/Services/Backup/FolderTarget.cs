namespace MatNote.Services.Backup;

/// <summary>A directory: the volume <c>/backups</c> of the image, which can be a NAS share that is mounted on the host.</summary>
public sealed class FolderTarget : IBackupTarget
{
    private readonly string _directory;

    public FolderTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BackupTargetException("The folder of the target is empty.");
        }

        _directory = Path.GetFullPath(path.Trim());
    }

    public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct)
    {
        var result = new List<BackupFile>();
        if (Directory.Exists(_directory))
        {
            try
            {
                foreach (var info in new DirectoryInfo(_directory).EnumerateFiles("matnote_*.zip"))
                {
                    if (BackupFileName.IsValid(info.Name))
                    {
                        result.Add(new BackupFile(info.Name, info.Length, info.LastWriteTimeUtc));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BackupTargetException($"The folder {_directory} cannot be read: {ex.Message}", ex);
            }
        }

        return Task.FromResult<IReadOnlyList<BackupFile>>(result);
    }

    public async Task UploadAsync(string name, Stream source, CancellationToken ct)
    {
        var final = PathOf(name);
        var part = final + ".part";
        try
        {
            Directory.CreateDirectory(_directory);
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(file, 81920, ct);
                await file.FlushAsync(ct);
            }

            File.Move(part, final, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(part);
            throw new BackupTargetException($"The backup could not be written to {_directory}: {Describe(ex)}", ex);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    public async Task DownloadAsync(string name, Stream destination, CancellationToken ct)
    {
        var path = PathOf(name);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await file.CopyToAsync(destination, 81920, ct);
        }
        catch (FileNotFoundException ex)
        {
            throw new BackupTargetException($"The backup {name} was not found in {_directory}.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new BackupTargetException($"The folder {_directory} does not exist.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException($"The backup {name} cannot be read: {Describe(ex)}", ex);
        }
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        var path = PathOf(name);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException($"The backup {name} cannot be deleted: {Describe(ex)}", ex);
        }

        return Task.CompletedTask;
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var probe = Path.Combine(_directory, ".matnote-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(probe, "MatNotes backup test", ct);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException($"The folder {_directory} cannot be written: {Describe(ex)}", ex);
        }

        var found = (await ListAsync(ct)).Count;
        return $"The folder {_directory} can be written. It holds {found} {(found == 1 ? "backup" : "backups")}.";
    }

    private string PathOf(string name)
    {
        if (!BackupFileName.IsValid(name))
        {
            throw new BackupTargetException("This is not the name of a backup file.");
        }

        return Path.Combine(_directory, name);
    }

    private static string Describe(Exception ex) => ex is UnauthorizedAccessException ? "access is denied (the folder is read-only or belongs to another user)." : ex.Message;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nothing more can be done for a leftover .part file
        }
    }
}

namespace MatNote.Services.Backup;

/// <summary>Makes the <see cref="IBackupTarget"/> of a configured target. The password of an SMB target is unprotected here and goes no further than the connection.</summary>
public sealed class BackupTargetFactory(SecretProtector secrets)
{
    public IBackupTarget Create(BackupTarget target)
        => Create(target, target.HasPassword ? secrets.Unprotect(target.Password) ?? throw new BackupTargetException(
            $"The stored password of the target \"{target.Name}\" cannot be read (the encryption keys have changed). Open the target and enter the password again.") : string.Empty);

    /// <summary>With an explicit password (the "Test connection" button tests what is in the form, before anything is saved).</summary>
    public IBackupTarget Create(BackupTarget target, string password) => target.Type switch
    {
        BackupTargetType.Folder => new FolderTarget(target.Path),
        BackupTargetType.Smb => new SmbTarget(new SmbSettings(target.Host.Trim(), target.Port, target.Share.Trim(), target.SubFolder, target.Username.Trim(), target.Domain.Trim(), password)),
        _ => throw new BackupTargetException("This kind of target is not known."),
    };

    /// <summary>The protected form of a password, for the configuration file.</summary>
    public string Protect(string plain) => plain.Length == 0 ? string.Empty : secrets.Protect(plain);
}

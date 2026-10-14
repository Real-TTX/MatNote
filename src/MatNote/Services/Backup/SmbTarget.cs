using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Client;

namespace MatNote.Services.Backup;

/// <summary>What it takes to reach a share: the password is plain here (it lives only in memory, for the length of one operation).</summary>
public sealed record SmbSettings(string Host, int Port, string Share, string SubFolder, string Username, string Domain, string Password);

/// <summary>
/// A share on a NAS or a server, over SMB 2/3 on TCP (SMBLibrary): no mount and no extra container needed. SMBLibrary is synchronous, so every operation runs on a thread
/// of its own with a time limit; when the limit is over the connection is closed, which ends whatever the thread was waiting for. Every operation opens its own connection and
/// closes it again (a backup is a rare, large operation: there is nothing to gain from keeping a connection).
/// </summary>
public sealed class SmbTarget(SmbSettings settings) : IBackupTarget
{
    private static readonly TimeSpan ShortLimit = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TransferLimit = TimeSpan.FromHours(6);

    private readonly string _folder = NormalizeFolder(settings.SubFolder);

    /// <summary>Checks the settings (the page uses it before it saves them); <c>null</c> when they are fine, else the field and a message.</summary>
    public static (string Field, string Message)? Validate(SmbSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            return ("Host", "Please enter the name or the address of the server.");
        }

        if (s.Host.Any(c => char.IsWhiteSpace(c) || c is '\\' or '/'))
        {
            return ("Host", "The server is a name or an address (nas.local, 192.168.1.20), without slashes.");
        }

        if (s.Port is < 1 or > 65535)
        {
            return ("Port", "The port is between 1 and 65535 (SMB uses 445).");
        }

        if (string.IsNullOrWhiteSpace(s.Share) || s.Share.Any(c => c is '\\' or '/' || char.IsControl(c)))
        {
            return ("Share", "Please enter the name of the share, without slashes.");
        }

        try
        {
            NormalizeFolder(s.SubFolder);
        }
        catch (BackupTargetException ex)
        {
            return ("SubFolder", ex.Message);
        }

        return null;
    }

    public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken ct) => RunAsync<IReadOnlyList<BackupFile>>(c => c.List(), ShortLimit, ct);

    public Task UploadAsync(string name, Stream source, CancellationToken ct)
    {
        CheckName(name);
        return RunAsync<object?>(c =>
        {
            c.Upload(name, source, ct);
            return null;
        }, TransferLimit, ct);
    }

    public Task DownloadAsync(string name, Stream destination, CancellationToken ct)
    {
        CheckName(name);
        return RunAsync<object?>(c =>
        {
            c.Download(name, destination, ct);
            return null;
        }, TransferLimit, ct);
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        CheckName(name);
        return RunAsync<object?>(c =>
        {
            c.Delete(name);
            return null;
        }, ShortLimit, ct);
    }

    public Task<string> TestConnectionAsync(CancellationToken ct) => RunAsync(c => c.Test(), ShortLimit, ct);

    private static void CheckName(string name)
    {
        if (!BackupFileName.IsValid(name))
        {
            throw new BackupTargetException("This is not the name of a backup file.");
        }
    }

    /// <summary>A folder inside the share, with backslashes and without a leading or trailing one. <c>..</c> and characters a share does not allow are refused.</summary>
    internal static string NormalizeFolder(string? folder)
    {
        var parts = (folder ?? string.Empty).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part is "." or ".." || part.Any(c => char.IsControl(c) || c is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
            {
                throw new BackupTargetException("The folder in the share must not contain .. or any of < > : \" | ? *");
            }
        }

        return string.Join('\\', parts);
    }

    private async Task<T> RunAsync<T>(Func<SmbConnection, T> work, TimeSpan limit, CancellationToken ct)
    {
        SmbConnection? connection = null;
        var task = Task.Run(
            () =>
            {
                using var c = SmbConnection.Open(settings, _folder);
                Volatile.Write(ref connection, c);
                return work(c);
            },
            CancellationToken.None);
        try
        {
            return await task.WaitAsync(limit, ct);
        }
        catch (TimeoutException ex)
        {
            Abandon(task, Volatile.Read(ref connection));
            throw new BackupTargetException($"The server {settings.Host} did not answer in time (limit: {(limit.TotalMinutes >= 1 ? (int)limit.TotalMinutes + " minutes" : (int)limit.TotalSeconds + " seconds")}).", ex);
        }
        catch (OperationCanceledException)
        {
            Abandon(task, Volatile.Read(ref connection));
            throw;
        }
    }

    /// <summary>Closes the connection so that the thread that waits on it ends, and makes sure nobody is left with its exception.</summary>
    private static void Abandon(Task task, SmbConnection? connection)
    {
        connection?.Dispose();
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>An SMB2 client that can connect to another port than 445 (the library has the method, but only for its own use).</summary>
    private sealed class PortClient(int responseTimeoutMs) : SMB2Client(responseTimeoutMs, true, false)
    {
        public bool ConnectTo(IPAddress address, int port) => Connect(address, SMBTransportType.DirectTCPTransport, port);
    }

    /// <summary>One connection to the share and the operations on the folder of the target. Synchronous: it always runs on a thread of its own.</summary>
    private sealed class SmbConnection : IDisposable
    {
        private readonly SmbSettings _settings;
        private readonly string _folder;
        private readonly PortClient _client = new(30_000);
        private ISMBFileStore? _store;
        private bool _disposed;

        private SmbConnection(SmbSettings settings, string folder)
        {
            _settings = settings;
            _folder = folder;
        }

        public static SmbConnection Open(SmbSettings settings, string folder)
        {
            var connection = new SmbConnection(settings, folder);
            try
            {
                connection.Connect();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private int ChunkSize(uint serverMax) => (int)Math.Clamp(serverMax, 4096u, 1024u * 1024u);

        private void Connect()
        {
            var address = Resolve();
            ProbePort(address);

            bool connected;
            try
            {
                connected = _client.ConnectTo(address, _settings.Port);
            }
            catch (Exception ex) when (ex is not BackupTargetException)
            {
                throw new BackupTargetException($"The connection to {_settings.Host}:{_settings.Port} failed: {ex.Message}", ex);
            }

            if (!connected)
            {
                throw new BackupTargetException($"The server {_settings.Host}:{_settings.Port} is reachable but does not speak SMB 2 or 3 (the old SMB 1 is not supported).");
            }

            var login = _client.Login(_settings.Domain, _settings.Username, _settings.Password);
            if (login != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The sign-in to {_settings.Host} failed: {Explain(login)}");
            }

            _store = _client.TreeConnect(_settings.Share, out var status);
            if (status != NTStatus.STATUS_SUCCESS || _store is null)
            {
                throw new BackupTargetException($"The share \"{_settings.Share}\" cannot be opened: {Explain(status)}");
            }
        }

        private IPAddress Resolve()
        {
            try
            {
                var addresses = Dns.GetHostAddresses(_settings.Host);
                return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault()
                    ?? throw new BackupTargetException($"The server name {_settings.Host} has no address.");
            }
            catch (SocketException ex)
            {
                throw new BackupTargetException($"The server name {_settings.Host} is not known (is the name right, and does this container see it?).", ex);
            }
        }

        /// <summary>The library connects without a limit; a plain connection first fails fast when nobody listens.</summary>
        private void ProbePort(IPAddress address)
        {
            using var probe = new TcpClient(address.AddressFamily);
            try
            {
                if (!probe.ConnectAsync(address, _settings.Port).Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new BackupTargetException($"The server {_settings.Host} did not answer on port {_settings.Port} (a firewall, or the wrong address?).");
                }
            }
            catch (AggregateException ex) when (ex.GetBaseException() is SocketException socket)
            {
                var why = socket.SocketErrorCode == SocketError.ConnectionRefused ? "refused the connection" : socket.Message;
                throw new BackupTargetException($"The server {_settings.Host} {why} on port {_settings.Port}. Is SMB switched on, and is the port right?", socket);
            }
        }

        private ISMBFileStore Store => _store ?? throw new InvalidOperationException("Not connected.");

        private string PathOf(string name) => _folder.Length == 0 ? name : _folder + "\\" + name;

        public IReadOnlyList<BackupFile> List()
        {
            var result = new List<BackupFile>();
            var status = Store.CreateFile(out var handle, out _, _folder, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
            if (status is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND or NTStatus.STATUS_NO_SUCH_FILE)
            {
                return result; // the folder is made by the first upload
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The folder \"{_folder}\" in the share cannot be read: {Explain(status)}");
            }

            try
            {
                status = Store.QueryDirectory(out var entries, handle, "matnote_*.zip", FileInformationClass.FileDirectoryInformation);
                if (status is not (NTStatus.STATUS_SUCCESS or NTStatus.STATUS_NO_MORE_FILES or NTStatus.STATUS_NO_SUCH_FILE))
                {
                    throw new BackupTargetException($"The folder cannot be listed: {Explain(status)}");
                }

                foreach (var info in entries ?? [])
                {
                    var entry = (FileDirectoryInformation)info;
                    if ((entry.FileAttributes & SMBLibrary.FileAttributes.Directory) == 0 && BackupFileName.IsValid(entry.FileName))
                    {
                        var modified = entry.LastWriteTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(entry.LastWriteTime, DateTimeKind.Utc) : entry.LastWriteTime.ToUniversalTime();
                        result.Add(new BackupFile(entry.FileName, entry.EndOfFile, modified));
                    }
                }
            }
            finally
            {
                Store.CloseFile(handle);
            }

            return result;
        }

        /// <summary>Creates the folder of the target (and the folders above it) when it is missing.</summary>
        private void EnsureFolder()
        {
            var path = string.Empty;
            foreach (var part in _folder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                path = path.Length == 0 ? part : path + "\\" + part;
                var status = Store.CreateFile(out var handle, out _, path, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN_IF, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new BackupTargetException($"The folder \"{path}\" cannot be created in the share: {Explain(status)}");
                }

                Store.CloseFile(handle);
            }
        }

        public void Upload(string name, Stream source, CancellationToken ct)
        {
            EnsureFolder();
            var part = PathOf(name + ".part");
            var status = Store.CreateFile(out var handle, out _, part, AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE, SMBLibrary.FileAttributes.Normal,
                ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The backup cannot be created on the share: {Explain(status)}");
            }

            var complete = false;
            try
            {
                var buffer = new byte[ChunkSize(_client.MaxWriteSize)];
                long offset = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var data = read == buffer.Length ? buffer : buffer[..read];
                    status = Store.WriteFile(out var written, handle, offset, data);
                    if (status != NTStatus.STATUS_SUCCESS || written != data.Length)
                    {
                        throw new BackupTargetException(status == NTStatus.STATUS_SUCCESS
                            ? "The share accepted only a part of the data."
                            : $"Writing to the share failed: {Explain(status)}");
                    }

                    offset += written;
                }

                // the file gets its final name only when it is complete
                status = Store.SetFileInformation(handle, new FileRenameInformationType2 { FileName = PathOf(name), ReplaceIfExists = true });
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new BackupTargetException($"The finished backup cannot be renamed on the share: {Explain(status)}");
                }

                complete = true;
            }
            finally
            {
                Store.CloseFile(handle);
                if (!complete)
                {
                    TryDelete(part);
                }
            }
        }

        public void Download(string name, Stream destination, CancellationToken ct)
        {
            var status = Store.CreateFile(out var handle, out _, PathOf(name), AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Normal, ShareAccess.Read,
                CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
            {
                throw new BackupTargetException($"The backup {name} was not found on the share.");
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The backup {name} cannot be opened: {Explain(status)}");
            }

            try
            {
                var chunk = ChunkSize(_client.MaxReadSize);
                long offset = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    status = Store.ReadFile(out var data, handle, offset, chunk);
                    if (status == NTStatus.STATUS_END_OF_FILE)
                    {
                        break;
                    }

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new BackupTargetException($"Reading from the share failed: {Explain(status)}");
                    }

                    if (data is null || data.Length == 0)
                    {
                        break;
                    }

                    destination.Write(data, 0, data.Length);
                    offset += data.Length;
                }
            }
            finally
            {
                Store.CloseFile(handle);
            }
        }

        public void Delete(string name)
        {
            var status = DeleteFile(PathOf(name));
            if (status is NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
            {
                throw new BackupTargetException($"The backup {name} was not found on the share.");
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The backup {name} cannot be deleted: {Explain(status)}");
            }
        }

        private NTStatus DeleteFile(string path)
        {
            var status = Store.CreateFile(out var handle, out _, path, AccessMask.DELETE, SMBLibrary.FileAttributes.Normal, ShareAccess.None,
                CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_DELETE_ON_CLOSE, null);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                status = Store.CloseFile(handle);
            }

            return status;
        }

        private void TryDelete(string path)
        {
            try
            {
                DeleteFile(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                // a leftover .part file is better than a second error that hides the first
            }
        }

        public string Test()
        {
            EnsureFolder();
            var probe = PathOf(".matnote-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            var status = Store.CreateFile(out var handle, out _, probe, AccessMask.GENERIC_WRITE | AccessMask.DELETE, SMBLibrary.FileAttributes.Normal, ShareAccess.None,
                CreateDisposition.FILE_CREATE, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_DELETE_ON_CLOSE, null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new BackupTargetException($"The share is reachable but cannot be written: {Explain(status)}");
            }

            try
            {
                status = Store.WriteFile(out _, handle, 0, System.Text.Encoding.ASCII.GetBytes("MatNotes backup test"));
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new BackupTargetException($"The share is reachable but writing failed: {Explain(status)}");
                }
            }
            finally
            {
                Store.CloseFile(handle); // FILE_DELETE_ON_CLOSE removes the test file
            }

            var found = List().Count;
            return $"Connected to {_settings.Host}:{_settings.Port}, share \"{_settings.Share}\"{(_folder.Length == 0 ? string.Empty : ", folder \"" + _folder + "\"")}. It can be written and holds {found} {(found == 1 ? "backup" : "backups")}.";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Quietly(() => _store?.Disconnect());
            Quietly(() => _client.Logoff());
            Quietly(() => _client.Disconnect());
        }

        private static void Quietly(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // closing a connection that is already gone
            }
        }
    }

    /// <summary>What an NT status means for an administrator.</summary>
    internal static string Explain(NTStatus status) => status switch
    {
        NTStatus.STATUS_LOGON_FAILURE => "the user name or the password is wrong.",
        NTStatus.STATUS_ACCOUNT_DISABLED => "the account is disabled.",
        NTStatus.STATUS_ACCOUNT_LOCKED_OUT => "the account is locked out (too many wrong passwords).",
        NTStatus.STATUS_PASSWORD_EXPIRED => "the password of the account has expired.",
        NTStatus.STATUS_ACCESS_DENIED => "access is denied (the account may not read or write here).",
        NTStatus.STATUS_BAD_NETWORK_NAME => "the server has no share of this name.",
        NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_OBJECT_PATH_NOT_FOUND or NTStatus.STATUS_NO_SUCH_FILE => "it does not exist.",
        NTStatus.STATUS_OBJECT_NAME_COLLISION => "it exists already.",
        NTStatus.STATUS_DISK_FULL => "the share is full.",
        NTStatus.STATUS_SHARING_VIOLATION => "the file is in use by another program.",
        NTStatus.STATUS_USER_SESSION_DELETED or NTStatus.STATUS_NETWORK_NAME_DELETED => "the server closed the connection.",
        NTStatus.STATUS_NOT_SUPPORTED => "the server does not support this.",
        _ => status.ToString(),
    };
}

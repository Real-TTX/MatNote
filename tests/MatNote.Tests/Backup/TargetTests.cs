using System.Text;
using MatNote.Services.Backup;

namespace MatNote.Tests.Backup;

public sealed class FolderTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "matnote-test-" + Guid.NewGuid().ToString("N"));
    private const string Name = "matnote_a1b2c3d4_20261014-023005Z.zip";

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Upload_creates_the_folder_and_leaves_no_part_file()
    {
        var target = new FolderTarget(Path.Combine(_root, "deep", "er"));

        await target.UploadAsync(Name, Bytes("hello"), CancellationToken.None);

        var files = Directory.GetFiles(Path.Combine(_root, "deep", "er"));
        Assert.Equal([Name], files.Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_failed_upload_leaves_neither_the_file_nor_a_part_file()
    {
        var target = new FolderTarget(_root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => target.UploadAsync(Name, new FailingStream(), CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Download_returns_what_was_uploaded()
    {
        var target = new FolderTarget(_root);
        await target.UploadAsync(Name, Bytes("the content"), CancellationToken.None);

        using var output = new MemoryStream();
        await target.DownloadAsync(Name, output, CancellationToken.None);

        Assert.Equal("the content", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task List_shows_only_backup_files_with_size()
    {
        var target = new FolderTarget(_root);
        await target.UploadAsync(Name, Bytes("12345"), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, Name + ".part"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "matnote_bad.zip"), "x");

        var list = await target.ListAsync(CancellationToken.None);

        var file = Assert.Single(list);
        Assert.Equal(Name, file.Name);
        Assert.Equal(5, file.Size);
    }

    [Fact]
    public async Task List_of_a_folder_that_does_not_exist_yet_is_empty()
    {
        Assert.Empty(await new FolderTarget(Path.Combine(_root, "missing")).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var target = new FolderTarget(_root);
        await target.UploadAsync(Name, Bytes("x"), CancellationToken.None);

        await target.DeleteAsync(Name, CancellationToken.None);

        Assert.Empty(await target.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside.zip")]
    [InlineData("..\\outside.zip")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/matnote_a1b2c3d4_20261014-023005Z.zip")]
    [InlineData("notes.txt")]
    public async Task A_name_that_is_not_a_backup_file_is_refused_everywhere(string name)
    {
        var target = new FolderTarget(Path.Combine(_root, "t"));
        Directory.CreateDirectory(Path.Combine(_root, "t"));
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.zip"), "secret");

        await Assert.ThrowsAsync<BackupTargetException>(() => target.DeleteAsync(name, CancellationToken.None));
        await Assert.ThrowsAsync<BackupTargetException>(() => target.DownloadAsync(name, new MemoryStream(), CancellationToken.None));
        await Assert.ThrowsAsync<BackupTargetException>(() => target.UploadAsync(name, Bytes("x"), CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(_root, "outside.zip")));
    }

    [Fact]
    public async Task Download_of_a_missing_backup_says_so()
    {
        var target = new FolderTarget(_root);
        Directory.CreateDirectory(_root);

        var ex = await Assert.ThrowsAsync<BackupTargetException>(() => target.DownloadAsync(Name, new MemoryStream(), CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Test_connection_writes_and_removes_a_test_file()
    {
        var target = new FolderTarget(_root);
        await target.UploadAsync(Name, Bytes("x"), CancellationToken.None);

        var message = await target.TestConnectionAsync(CancellationToken.None);

        Assert.Contains("1 backup", message);
        Assert.Equal([Name], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void An_empty_folder_is_refused()
    {
        Assert.Throws<BackupTargetException>(() => new FolderTarget("  "));
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ == 0)
            {
                buffer[offset] = 1;
                return 1;
            }

            throw new InvalidOperationException("the source broke");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class SmbTargetTests
{
    private static SmbSettings Settings(string host = "nas.local", int port = 445, string share = "backup", string folder = "matnotes") => new(host, port, share, folder, "user", string.Empty, "secret");

    [Fact]
    public void Good_settings_are_accepted()
    {
        Assert.Null(SmbTarget.Validate(Settings()));
        Assert.Null(SmbTarget.Validate(Settings(host: "192.168.1.20", port: 4450, folder: "a/b\\c")));
        Assert.Null(SmbTarget.Validate(Settings(folder: string.Empty)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nas local")]
    [InlineData("\\\\nas")]
    [InlineData("nas/share")]
    public void A_bad_host_is_refused(string host)
    {
        Assert.NotNull(SmbTarget.Validate(Settings(host: host)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void A_bad_port_is_refused(int port)
    {
        Assert.NotNull(SmbTarget.Validate(Settings(port: port)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void A_bad_share_is_refused(string share)
    {
        Assert.NotNull(SmbTarget.Validate(Settings(share: share)));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../b")]
    [InlineData("a\\..")]
    [InlineData("a/b:c")]
    [InlineData("a/b*")]
    public void A_folder_that_climbs_out_or_has_forbidden_characters_is_refused(string folder)
    {
        Assert.NotNull(SmbTarget.Validate(Settings(folder: folder)));
    }

    [Theory]
    [InlineData("../matnote_a1b2c3d4_20261014-023005Z.zip")]
    [InlineData("..\\x")]
    [InlineData("notes.txt")]
    public async Task A_name_that_is_not_a_backup_file_is_refused_before_any_connection_is_made(string name)
    {
        // the host does not exist: if a connection were tried the exception would say so instead
        var target = new SmbTarget(Settings(host: "does-not-exist.invalid"));

        var ex = await Assert.ThrowsAsync<BackupTargetException>(() => target.DeleteAsync(name, CancellationToken.None));
        Assert.Contains("not the name of a backup file", ex.Message);
        await Assert.ThrowsAsync<BackupTargetException>(() => target.DownloadAsync(name, new MemoryStream(), CancellationToken.None));
        await Assert.ThrowsAsync<BackupTargetException>(() => target.UploadAsync(name, new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_host_gives_a_readable_message()
    {
        var target = new SmbTarget(Settings(host: "does-not-exist.invalid"));

        var ex = await Assert.ThrowsAsync<BackupTargetException>(() => target.TestConnectionAsync(CancellationToken.None));

        Assert.Contains("does-not-exist.invalid", ex.Message);
    }

    [Fact]
    public async Task A_port_where_nobody_listens_gives_a_readable_message()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var ex = await Assert.ThrowsAsync<BackupTargetException>(() => new SmbTarget(Settings(host: "127.0.0.1", port: port)).TestConnectionAsync(CancellationToken.None));

        Assert.Contains("127.0.0.1", ex.Message);
    }

    [Fact]
    public void The_location_of_a_target_has_no_secrets()
    {
        var t = new BackupTarget { Type = BackupTargetType.Smb, Host = "nas", Port = 4450, Share = "backup", SubFolder = "a/b", Username = "me", Password = "protected-text" };

        Assert.Equal(@"\\nas:4450\backup\a\b", t.Location);
        Assert.DoesNotContain("protected-text", t.Location);
        Assert.DoesNotContain("me", t.Location);
    }
}

public class PgConnectionTests
{
    [Fact]
    public void The_parts_of_a_connection_string()
    {
        var c = PgConnection.Parse("Host=postgres;Port=5433;Database=matnote;Username=app;Password=pw;SSL Mode=Require");

        Assert.Equal("postgres", c.Host);
        Assert.Equal(5433, c.Port);
        Assert.Equal("matnote", c.Database);
        Assert.Equal("app", c.Username);
        Assert.Equal("pw", c.Password);
        Assert.Equal("require", c.SslMode);
    }

    [Fact]
    public void The_password_is_in_the_environment_and_never_in_the_arguments()
    {
        var c = PgConnection.Parse("Host=postgres;Database=matnote;Username=app;Password=very-secret");

        Assert.Equal("very-secret", c.Environment["PGPASSWORD"]);
        Assert.DoesNotContain(c.Arguments, a => a.Contains("very-secret"));
        Assert.Contains("--no-password", c.Arguments);
        Assert.Equal(5432, c.Port);
    }

    [Fact]
    public void A_connection_string_without_a_database_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PgConnection.Parse("Host=postgres;Username=app"));
    }

    [Fact]
    public async Task A_missing_program_gives_a_readable_message()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PgTools.RunAsync("pg_dump_does_not_exist", [], new Dictionary<string, string>(), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Contains("postgresql-client", ex.Message);
    }
}

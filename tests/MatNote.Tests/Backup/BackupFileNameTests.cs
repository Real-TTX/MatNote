using MatNote.Services.Backup;

namespace MatNote.Tests.Backup;

public class BackupFileNameTests
{
    [Fact]
    public void Create_makes_a_sortable_utc_name()
    {
        var name = BackupFileName.Create("a1b2c3d4", new DateTime(2026, 10, 14, 2, 30, 5, DateTimeKind.Utc));

        Assert.Equal("matnote_a1b2c3d4_20261014-023005Z.zip", name);
    }

    [Fact]
    public void Names_sort_by_time()
    {
        var older = BackupFileName.Create("a1b2c3d4", new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc));
        var newer = BackupFileName.Create("a1b2c3d4", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(string.CompareOrdinal(older, newer) < 0);
    }

    [Fact]
    public void TryParse_round_trips()
    {
        var utc = new DateTime(2026, 10, 14, 2, 30, 5, DateTimeKind.Utc);

        Assert.True(BackupFileName.TryParse(BackupFileName.Create("prerestore", utc), out var id, out var parsed));
        Assert.Equal("prerestore", id);
        Assert.Equal(utc, parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Theory]
    [InlineData("../matnote_a1b2c3d4_20261014-023005Z.zip")]
    [InlineData("..\\matnote_a1b2c3d4_20261014-023005Z.zip")]
    [InlineData("sub/matnote_a1b2c3d4_20261014-023005Z.zip")]
    [InlineData("/etc/passwd")]
    [InlineData("matnote_a1b2c3d4_20261014-023005Z.zip.part")]
    [InlineData("matnote_a1b2c3d4_20261014-023005Z.zip\n")]
    [InlineData("matnote_a1b2c3d4_20261014-023005Z.zip ")]
    [InlineData("matnote__20261014-023005Z.zip")]
    [InlineData("matnote_A1B2C3D4_20261014-023005Z.zip")]
    [InlineData("matnote_a1b2c3d4_20261314-023005Z.zip")]
    [InlineData("matnote_a1b2c3d4_20261014-023005.zip")]
    [InlineData("matnote_a1_20261014-023005Z.zip")]
    [InlineData("")]
    [InlineData("matnote_a1b2c3d4_20261014-023005Z.zip\0.txt")]
    public void Anything_else_is_refused(string name)
    {
        Assert.False(BackupFileName.IsValid(name));
        Assert.False(BackupFileName.TryParse(name, out _, out _));
    }

    [Fact]
    public void Null_is_refused()
    {
        Assert.False(BackupFileName.IsValid(null));
        Assert.False(BackupFileName.TryParse(null, out _, out _));
    }

    [Theory]
    [InlineData("a1b2c3d4", true)]
    [InlineData("prerestore", true)]
    [InlineData("ab", false)]
    [InlineData("A1B2C3D4", false)]
    [InlineData("a_b2c3d4", false)]
    [InlineData("", false)]
    public void Schedule_ids(string id, bool valid)
    {
        Assert.Equal(valid, BackupFileName.IsScheduleId(id));
    }

    [Fact]
    public void Create_refuses_a_bad_schedule_id()
    {
        Assert.Throws<ArgumentException>(() => BackupFileName.Create("../x", DateTime.UtcNow));
    }

    [Theory]
    [InlineData("app.json", true)]
    [InlineData("backup.json", true)]
    [InlineData("app.json.tmp", false)]
    [InlineData("app.json.broken-20261014120000", false)]
    [InlineData("../app.json", false)]
    [InlineData("a/b.json", false)]
    [InlineData(".json", false)]
    [InlineData("app.json\n", false)]
    public void Config_names_in_the_archive(string name, bool ok)
    {
        Assert.Equal(ok, BackupArchive.IsConfigName(name));
    }

    [Theory]
    [InlineData("key-3f2a5c10-1111-2222-3333-444455556666.xml", true)]
    [InlineData("key.xml.tmp", false)]
    [InlineData("../key.xml", false)]
    [InlineData("a\\b.xml", false)]
    [InlineData("", false)]
    public void Key_names_in_the_archive(string name, bool ok)
    {
        Assert.Equal(ok, BackupArchive.IsKeyName(name));
    }
}

using System.IO.Compression;
using System.Text;
using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>The files attached to a note go along in an export when asked: one note becomes a zip, a folder zip gets a directory next to each note.</summary>
public class AttachmentExportTests
{
    private static readonly ExportOptions With = new(false, 3, true);

    private static Kit.Store StoreWith(long noteId, params (string Name, string Text)[] files)
    {
        var store = new Kit.Store();
        store.Attachments[noteId] = files.Select(f => (f.Name, Encoding.UTF8.GetBytes(f.Text))).ToList();
        return store;
    }

    private static Dictionary<string, string> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public async Task A_note_without_attachments_is_exported_as_before()
    {
        var note = Kit.Md(1, "Plan", "text");
        var result = await Kit.Export(note, ExportFormat.Markdown, With, store: new Kit.Store());
        Assert.Equal("Plan.md", result.FileName);
        Assert.Contains("text", Kit.Utf8(result));
    }

    [Fact]
    public async Task The_option_off_leaves_the_attachments_out()
    {
        var note = Kit.Md(1, "Plan", "text");
        var result = await Kit.Export(note, ExportFormat.Markdown, Kit.Plain, store: StoreWith(1, ("a.txt", "A")));
        Assert.Equal("Plan.md", result.FileName);
    }

    [Fact]
    public async Task A_note_with_attachments_becomes_a_zip_with_the_file_and_a_directory()
    {
        var note = Kit.Md(1, "Plan", "text");
        var result = await Kit.Export(note, ExportFormat.Markdown, With, store: StoreWith(1, ("a.txt", "A"), ("a.txt", "second"), ("../evil.txt", "E")));
        Assert.Equal("Plan.zip", result.FileName);
        Assert.Equal("application/zip", result.ContentType);
        var entries = Entries(result.Data);
        Assert.Contains("text", entries["Plan.md"]);
        Assert.Equal("A", entries["attachments/a.txt"]);
        Assert.Equal("second", entries["attachments/a (2).txt"]);
        Assert.Equal("E", entries["attachments/evil.txt"]);
        Assert.All(entries.Keys, k => Assert.DoesNotContain("..", k));
    }

    [Fact]
    public async Task A_folder_zip_gets_a_directory_next_to_each_note_with_attachments()
    {
        var a = Kit.Md(1, "Plan", "A text");
        var b = Kit.Md(2, "Other", "B text");
        var store = StoreWith(1, ("x.pdf", "PDF"));
        var result = await Kit.Exporter(store).ExportManyAsync("Folder", [("Sub", a), ("Sub", b)], ExportFormat.Markdown, With, _ => Task.FromResult<Note?>(null));
        var entries = Entries(result.Data);
        Assert.Contains("A text", entries["Sub/Plan.md"]);
        Assert.Contains("B text", entries["Sub/Other.md"]);
        Assert.Equal("PDF", entries["Sub/Plan - attachments/x.pdf"]);
        Assert.DoesNotContain(entries.Keys, k => k.Contains("Other - attachments"));
    }
}

using System.IO.Compression;
using System.Text;
using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>A folder as a zip that MatNotes can import again, and a plain zip of files as an import.</summary>
public class FolderArchiveTests
{
    private static readonly ArchiveFolder[] Tree =
    [
        new(1, null, "Work", null),
        new(2, 1, "Prompts", "#c34f00"),
        new(3, 2, "Deep", null),
    ];

    private static Kit.Store Store(params (long Id, string Name, string Text)[] attachments)
    {
        var store = new Kit.Store();
        foreach (var a in attachments)
        {
            store.Attachments[a.Id] = [(a.Name, Encoding.UTF8.GetBytes(a.Text))];
        }

        return store;
    }

    private static Stream Zip(params (string Name, string Text)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }

        memory.Position = 0;
        return memory;
    }

    [Fact]
    public async Task An_archive_comes_back_as_it_went_out()
    {
        var rich = Kit.Rich(10, "Plan", "<p>See " + Kit.Link(11, "Other") + "</p>");
        rich.OwnerId = 7;
        rich.IsFavorite = true;
        rich.FolderId = 2;
        var md = Kit.Md(11, "Other", "# Title\n[Plan](note:10)");
        md.OwnerId = 7;
        md.FolderId = 3;
        var top = Kit.Txt(12, "Top level", "plain");
        top.OwnerId = 7;
        top.FolderId = 1;
        var file = Kit.Note(13, NoteType.Docx, "Report");
        file.OwnerId = 7;
        file.FolderId = 2;
        file.FileName = "Report.docx";
        var store = Store((10, "a.txt", "attached"));
        store.Files[13] = [1, 2, 3];
        store.Tags[10] = ["prompt", "ideas"];

        var made = await FolderArchive.WriteAsync("Work", 1, Tree, [rich, md, top, file], store, viewerId: 7);
        Assert.Equal("Work.matnotes.zip", made.FileName);

        var plan = FolderArchive.Read(new MemoryStream(made.Data), 1_000_000, "x");
        Assert.True(plan.IsArchive);
        Assert.Equal("Work", plan.Name);
        Assert.Empty(plan.Skipped);
        Assert.Equal(["Work", "Prompts", "Deep"], plan.Folders.Select(f => f.Name));
        Assert.Equal("#c34f00", plan.Folders.Single(f => f.Name == "Prompts").Color);
        Assert.Equal("f2", plan.Folders.Single(f => f.Name == "Deep").ParentKey);

        var plan10 = plan.Notes.Single(n => n.OldId == 10);
        Assert.Equal(NoteType.RichText, plan10.Type);
        Assert.True(plan10.Favorite);
        Assert.Equal("f2", plan10.FolderKey);
        Assert.Equal(["prompt", "ideas"], plan10.Tags);
        Assert.Equal(("a.txt", "attached"), (plan10.Attachments.Single().Name, Encoding.UTF8.GetString(plan10.Attachments.Single().Data)));
        Assert.Contains("data-note-id=\"11\"", plan10.Content);
        Assert.Equal("f3", plan.Notes.Single(n => n.OldId == 11).FolderKey);
        Assert.Equal(NoteType.Markdown, plan.Notes.Single(n => n.OldId == 11).Type);
        Assert.Equal("f1", plan.Notes.Single(n => n.OldId == 12).FolderKey);
        var docx = plan.Notes.Single(n => n.OldId == 13);
        Assert.Equal(NoteType.File, docx.Type);
        Assert.Equal("Report.docx", docx.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, docx.File);
    }

    [Fact]
    public async Task The_zip_is_readable_without_MatNotes_directories_and_file_names()
    {
        var note = Kit.Md(11, "Other", "text");
        note.FolderId = 3;
        var made = await FolderArchive.WriteAsync("Work", 1, Tree, [note], new Kit.Store(), 1);
        using var zip = new ZipArchive(new MemoryStream(made.Data), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "Prompts/Deep/Other.md");
        Assert.Contains(zip.Entries, e => e.FullName == FolderArchive.ManifestName);
    }

    [Fact]
    public async Task Tags_of_somebody_else_are_not_taken_along()
    {
        var note = Kit.Md(11, "Other", "text");
        note.OwnerId = 99;
        var store = new Kit.Store();
        store.Tags[11] = ["secret"];
        var made = await FolderArchive.WriteAsync("Work", 1, Tree, [note], store, viewerId: 7);
        Assert.Empty(FolderArchive.Read(new MemoryStream(made.Data), 1_000_000, "x").Notes.Single().Tags);
    }

    [Fact]
    public void A_plain_zip_becomes_folders_and_notes()
    {
        var plan = FolderArchive.Read(Zip(
            ("readme.txt", "hello"),
            ("Docs/guide.md", "# Guide"),
            ("Docs/Old/page.html", "<p>x</p>"),
            ("Docs/data.bin", "bytes"),
            ("Docs/empty.txt", ""),
            ("__MACOSX/Docs/._guide.md", "junk"),
            ("Docs/.DS_Store", "junk"),
            ("../evil.txt", "x")), 1_000_000, "My zip");
        Assert.False(plan.IsArchive);
        Assert.Equal(["Docs", "Old"], plan.Folders.Select(f => f.Name));
        Assert.Equal("Docs", plan.Folders.Single(f => f.Name == "Old").ParentKey);
        Assert.Equal(NoteType.PlainText, plan.Notes.Single(n => n.Title == "readme").Type);
        Assert.Null(plan.Notes.Single(n => n.Title == "readme").FolderKey);
        Assert.Equal(NoteType.Markdown, plan.Notes.Single(n => n.Title == "guide").Type);
        Assert.Equal(NoteType.RichText, plan.Notes.Single(n => n.Title == "page").Type);
        var bin = plan.Notes.Single(n => n.Title == "data");
        Assert.Equal((NoteType.File, "data.bin"), (bin.Type, bin.FileName));
        Assert.DoesNotContain(plan.Notes, n => n.Title is "empty" or "evil" or "._guide");
        Assert.Contains(plan.Skipped, s => s.Contains("empty"));
        Assert.Contains(plan.Skipped, s => s.Contains("evil"));
    }

    [Fact]
    public void A_file_that_is_too_big_is_skipped_and_named()
    {
        var plan = FolderArchive.Read(Zip(("small.txt", "ok"), ("big.txt", new string('x', 500))), 100, "x");
        Assert.Single(plan.Notes);
        Assert.Contains(plan.Skipped, s => s.Contains("big.txt") && s.Contains("too big"));
    }

    [Fact]
    public void A_broken_manifest_is_refused()
        => Assert.Throws<InvalidDataException>(() => FolderArchive.Read(Zip((FolderArchive.ManifestName, "{ not json")), 1000, "x"));

    [Fact]
    public void Something_that_is_not_a_zip_is_refused()
        => Assert.Throws<InvalidDataException>(() => FolderArchive.Read(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 1000, "x"));

    [Fact]
    public void Links_follow_the_new_ids_and_links_out_of_the_import_lose_their_target()
    {
        long? Map(long id) => id == 11 ? 211 : null;
        var rich = FolderArchive.RemapLinks(NoteType.RichText, "<p>" + Kit.Link(11, "Kept") + " and " + Kit.Link(99, "Gone") + "</p>", Map);
        Assert.Contains("data-note-id=\"211\"", rich);
        Assert.Contains("href=\"/Notes/View?id=211\"", rich);
        Assert.Contains("Gone", rich);
        Assert.DoesNotContain("99", rich);
        Assert.Equal("[Kept](note:211) and Gone", FolderArchive.RemapLinks(NoteType.Markdown, "[Kept](note:11) and [Gone](note:99)", Map));
        Assert.Equal("Kept and Gone", FolderArchive.RemapLinks(NoteType.PlainText, "[Kept](note:11) and [Gone](note:99)", _ => null));
        Assert.True(FolderArchive.HasLinks(NoteType.Markdown, "[a](note:1)"));
        Assert.False(FolderArchive.HasLinks(NoteType.Markdown, "no links"));
    }
}

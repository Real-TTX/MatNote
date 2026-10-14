using MatNote.Data;
using MatNote.Services;

namespace MatNote.Tests;

public class ListAndConfigTests
{
    private static ToolbarModel Toolbar(string? search = null, string? sort = null, bool descending = false, string? type = null) => new()
    {
        Id = "t",
        Action = "/Notes",
        Search = search,
        Sort = sort,
        Descending = descending,
        DefaultSort = "updated",
        DefaultDescending = true,
        Filters = [new("type", "Type", [new("Pdf", "PDF")], type)],
        SortOptions = [new("updated", "Last changed"), new("title", "Title")],
        Keep = new Dictionary<string, string?> { ["folder"] = "7" },
        Noun = "notes",
    };

    [Fact]
    public void The_default_sort_is_not_a_tool_in_effect()
    {
        Assert.False(Toolbar(sort: "updated", descending: true).SortActive);
        Assert.True(Toolbar(sort: "updated", descending: false).SortActive);
        Assert.True(Toolbar(sort: "title", descending: true).SortActive);
    }

    [Fact]
    public void Urls_keep_what_the_list_is_about_and_drop_what_is_removed()
    {
        var bar = Toolbar(search: "a b", type: "Pdf", sort: "title");
        Assert.Equal("/Notes?folder=7&q=a+b&type=Pdf&sort=title&dir=asc", bar.UrlWithout(string.Empty));
        Assert.Equal("/Notes?folder=7&type=Pdf&sort=title&dir=asc", bar.UrlWithout("q"));
        Assert.Equal("/Notes?folder=7&q=a+b&sort=title&dir=asc", bar.UrlWithout("type"));
        Assert.Equal("/Notes?folder=7", bar.UrlReset());
        Assert.EndsWith("&page=3", bar.UrlWithPage(3));
        Assert.DoesNotContain("page=", bar.UrlWithPage(1));
    }

    [Fact]
    public void Counting_uses_the_singular_for_one()
    {
        Assert.Equal("1 note", Toolbar().Counted(1));
        Assert.Equal("0 notes", Toolbar().Counted(0));
        Assert.Equal("12 notes", Toolbar().Counted(12));
    }

    [Theory]
    [InlineData(1, 10, new[] { 1, 2, 3, 0, 10 })]
    [InlineData(5, 10, new[] { 1, 0, 3, 4, 5, 6, 7, 0, 10 })]
    [InlineData(10, 10, new[] { 1, 0, 8, 9, 10 })]
    [InlineData(1, 1, new[] { 1 })]
    public void The_pager_shows_first_last_and_the_neighbours(int page, int pages, int[] expected)
    {
        var pager = new PagerModel { Page = page, PageSize = 10, Total = pages * 10, Url = p => "?page=" + p };
        Assert.Equal(pages, pager.Pages);
        Assert.Equal(expected, pager.Window());
    }

    [Fact]
    public void The_pager_counts_from_and_to()
    {
        var pager = new PagerModel { Page = 3, PageSize = 25, Total = 60, Url = p => "?" };
        Assert.Equal(51, pager.From);
        Assert.Equal(60, pager.To);
    }

    [Theory]
    [InlineData("report.docx", NoteType.Docx, false)]
    [InlineData("Sheet.XLSX", NoteType.Xlsx, false)]
    [InlineData("a.pdf", NoteType.Pdf, false)]
    [InlineData("a.txt", NoteType.PlainText, true)]
    [InlineData("a.md", NoteType.Markdown, true)]
    [InlineData("a.html", NoteType.RichText, true)]
    [InlineData("archive.zip", NoteType.File, false)]
    [InlineData("noextension", NoteType.File, false)]
    public void A_file_becomes_the_note_type_of_its_extension(string name, NoteType type, bool textImport)
        => Assert.Equal((type, textImport), NoteService.TypeFor(name));

    [Fact]
    public void Sizes_are_written_for_people()
    {
        Assert.Equal("512 B", Ui.Size(512));
        Assert.Equal("1.5 KB", Ui.Size(1536));
        Assert.Equal("2 MB", Ui.Size(2 * 1024 * 1024));
    }

    [Theory]
    [InlineData("#c34f00", true)]
    [InlineData("#C34F00", true)]
    [InlineData("c34f00", false)]
    [InlineData("#fff", false)]
    [InlineData("red", false)]
    [InlineData("#c34f00;x", false)]
    [InlineData(null, false)]
    public void Only_a_full_hex_colour_is_a_colour(string? value, bool expected)
        => Assert.Equal(expected, Ui.IsColour(value));

    [Fact]
    public void Icons_with_a_bad_name_are_refused()
        => Assert.Throws<ArgumentException>(() => Ui.Icon("x\"><script>"));

    [Theory]
    [InlineData("short", false)]
    [InlineData("long-enough-password", true)]
    [InlineData("alexalexalexalex", false)]
    public void Passwords_need_length_and_must_not_contain_the_name(string password, bool ok)
        => Assert.Equal(ok, PasswordService.Check(password, "alex") is null);

    [Fact]
    public void Passwords_are_hashed_with_a_salt_and_verified()
    {
        var service = new PasswordService();
        var a = service.Hash("long-enough-password");
        var b = service.Hash("long-enough-password");
        Assert.NotEqual(a, b);
        Assert.True(service.Verify(a, "long-enough-password"));
        Assert.False(service.Verify(a, "long-enough-passworD"));
        Assert.False(service.Verify(null, "x"));
    }

    [Fact]
    public void Config_is_written_on_the_first_start_and_changes_are_saved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "matnote-test-" + Guid.NewGuid());
        try
        {
            var paths = new AppPaths(dir);
            var store = new ConfigStore<AppConfig>(paths, "app.json");
            Assert.True(File.Exists(Path.Combine(paths.Config, "app.json")));
            Assert.False(store.Value.AllowRegistration);
            store.Update(c => c.AllowRegistration = true);
            Assert.True(new ConfigStore<AppConfig>(paths, "app.json").Value.AllowRegistration);
            Assert.Contains("allowRegistration", File.ReadAllText(Path.Combine(paths.Config, "app.json")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_config_file_that_cannot_be_read_is_kept_and_replaced_by_the_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "matnote-test-" + Guid.NewGuid());
        try
        {
            var paths = new AppPaths(dir);
            File.WriteAllText(Path.Combine(paths.Config, "app.json"), "{ this is not json");
            var store = new ConfigStore<AppConfig>(paths, "app.json");
            Assert.Equal(14, store.Value.SessionDays);
            Assert.Single(Directory.GetFiles(paths.Config, "app.json.broken-*"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

using System.Text.RegularExpressions;

namespace MatNote.Tests;

/// <summary>
/// The UI rules of the design system, held by tests: pages are made of toolkit classes only (no inline style, no inline script, no event-handler attribute),
/// every icon exists, the toolkit files are untouched copies, the controls have one code base.
/// </summary>
public partial class MarkupTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MatNote.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("MatNote.slnx not found");
    }

    private static string Src(string relative) => Path.Combine(Root, "src", "MatNote", relative);

    public static IEnumerable<object[]> Views()
        => Directory.GetFiles(Src("Pages"), "*.cshtml", SearchOption.AllDirectories).Select(f => new object[] { Path.GetRelativePath(Root, f) });

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyle();

    [GeneratedRegex(@"<script(?![^>]*\ssrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"\son[a-z]+\s*=\s*[""']", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandler();

    [GeneratedRegex(@"Ui\.Icon\(\s*""([a-z0-9-]+)""")]
    private static partial Regex IconCall();

    [GeneratedRegex(@"#mat-i-([a-z0-9-]+)")]
    private static partial Regex IconRef();

    [GeneratedRegex(@"id=""mat-i-([a-z0-9-]+)""")]
    private static partial Regex IconDefinition();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex LiteralColour();

    [Theory]
    [MemberData(nameof(Views))]
    public void A_page_has_no_inline_style_script_or_event_handler(string view)
    {
        var text = Read(view);
        Assert.DoesNotMatch(InlineStyle(), text);
        Assert.DoesNotMatch(InlineScript(), text);
        Assert.DoesNotMatch(EventHandler(), text);
    }

    [Fact]
    public void Every_icon_that_is_used_exists_in_a_sprite()
    {
        var sprite = Read("src/MatNote/wwwroot/matui/matui-icons.svg") + Read("src/MatNote/wwwroot/img/app-icons.svg");
        var known = IconDefinition().Matches(sprite).Select(m => m.Groups[1].Value).ToHashSet();
        var used = new HashSet<string>();
        foreach (var file in Directory.GetFiles(Src(string.Empty), "*.*", SearchOption.AllDirectories).Where(f => (f.EndsWith(".cs") || f.EndsWith(".cshtml") || f.EndsWith("editor.js")) && !f.Contains("Migrations") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in IconCall().Matches(text)) { used.Add(m.Groups[1].Value); }
            foreach (Match m in IconRef().Matches(text)) { used.Add(m.Groups[1].Value); }
        }

        // names the code builds from data (the icon of a note type) are listed here
        foreach (var name in new[] { "file-text", "type", "code", "table", "book", "paperclip" })
        {
            used.Add(name);
        }

        Assert.Empty(used.Where(n => !known.Contains(n)).Order());
    }

    [Fact]
    public void The_app_stylesheet_has_one_literal_colour_at_most_the_accent()
    {
        var css = Read("src/MatNote/wwwroot/css/app.css");
        var colours = LiteralColour().Matches(css).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
        Assert.Equal(["#c34f00"], colours);
    }

    [Fact]
    public void The_accent_is_orange_and_white_text_on_it_reaches_AA()
    {
        static double L(string hex)
        {
            double Ch(int i) { var v = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(1) + 0.7152 * Ch(3) + 0.0722 * Ch(5);
        }

        var accent = Regex.Match(Read("src/MatNote/wwwroot/css/app.css"), @"--mat-app-accent:\s*(#[0-9a-fA-F]{6})").Groups[1].Value;
        Assert.True(1.05 / (L(accent) + 0.05) >= 4.5, accent + " is too light for white text");
        var r = Convert.ToInt32(accent.Substring(1, 2), 16); var g = Convert.ToInt32(accent.Substring(3, 2), 16); var b = Convert.ToInt32(accent.Substring(5, 2), 16);
        Assert.True(r > 150 && g is > 40 and < 130 && b < 40, accent + " is not orange");
    }

    [Fact]
    public void The_toolkit_is_a_copy_of_the_parts_in_order_and_names_its_source()
    {
        var css = Read("src/MatNote/wwwroot/matui/matui.css");
        Assert.StartsWith("/* MatUi", css);
        var tokens = css.IndexOf("MatUi · tokens", StringComparison.Ordinal);
        var shell = css.IndexOf("MatUi · shell", StringComparison.Ordinal);
        var apps = css.IndexOf("MatUi · apps", StringComparison.Ordinal);
        Assert.True(0 < tokens && tokens < shell && shell < apps);
        Assert.Contains("MatUi boot", Read("src/MatNote/wwwroot/matui/matui.boot.js"));
    }

    [Fact]
    public void Tables_label_their_cells_for_the_card_view_on_a_phone()
    {
        foreach (var view in Views().Select(v => (string)v[0]))
        {
            var text = Read(view);
            foreach (Match row in Regex.Matches(text, @"<tr>(.*?)</tr>", RegexOptions.Singleline))
            {
                foreach (Match cell in Regex.Matches(row.Groups[1].Value, @"<td(?<attrs>[^>]*)>"))
                {
                    var attrs = cell.Groups["attrs"].Value;
                    Assert.True(attrs.Contains("data-label") || attrs.Contains("mat-table__cell-actions"), $"{view}: a td without data-label: <td{attrs}>");
                }
            }
        }
    }

    [Fact]
    public void Every_list_page_has_a_toolbar_and_a_pager()
    {
        foreach (var view in Views().Select(v => (string)v[0]).Where(v => Read(v).Contains("<mn-table")))
        {
            var text = Read(view);
            if (text.Contains("Admin/Backups", StringComparison.Ordinal) || view.Contains("Security") || view.Contains("Edit.cshtml") || view.Contains("History.cshtml") || view.Contains("Share.cshtml"))
            {
                continue; // a table inside a card of an edit page, not a list page
            }

            Assert.True(text.Contains("_Toolbar") && text.Contains("_Pager"), $"{view} is a list without a toolbar or a pager");
        }
    }

    [Fact]
    public void Delete_is_on_the_edit_page_never_in_a_list()
    {
        foreach (var view in Views().Select(v => (string)v[0]).Where(v => v.EndsWith("Index.cshtml") && !v.Contains("Trash")))
        {
            Assert.DoesNotContain("mat-btn--danger", Read(view));
        }
    }

    [Fact]
    public void Html_is_served_with_a_policy_that_forbids_inline_script_and_foreign_content()
    {
        var infrastructure = Read("src/MatNote/Infrastructure.cs");
        Assert.Contains("script-src 'self'", infrastructure);
        Assert.Contains("style-src 'self'", infrastructure);
        Assert.DoesNotContain("unsafe-inline", infrastructure);
        Assert.DoesNotContain("http://", Read("src/MatNote/Pages/Shared/_Layout.cshtml").Replace("http://www.w3.org", string.Empty));
    }

    [Fact]
    public void The_layout_has_the_account_logout_and_theme_at_the_bottom_of_the_menu()
    {
        var layout = Read("src/MatNote/Pages/Shared/_Layout.cshtml");
        var foot = layout[layout.IndexOf("mat-nav__foot", StringComparison.Ordinal)..];
        Assert.Contains("mat-user", foot);
        Assert.Contains("data-theme-toggle", foot);
        Assert.Contains("/Account/Logout", layout);
    }
}

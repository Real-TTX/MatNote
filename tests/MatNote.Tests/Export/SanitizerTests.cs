using System.Text.RegularExpressions;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

public partial class SanitizerTests
{
    private static readonly NotePreview Preview = Kit.Preview();

    [GeneratedRegex(
        @"<\s*(script|svg|iframe|object|embed|form|img|style|meta|link|base|math|input|button|template|noscript|textarea|select|video|audio|frame)|javascript\s*:|vbscript\s*:|\son\w+\s*=|\sstyle\s*=|expression\s*\(|\sdata:|srcdoc|srcset|formaction|xlink:href",
        RegexOptions.IgnoreCase)]
    private static partial Regex Danger();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<SCRIPT SRC=//evil.example/x.js></SCRIPT>",
        "<ScRiPt>alert(1)</sCrIpT>",
        "<img src=x onerror=alert(1)>",
        "<IMG SRC=\"javascript:alert(1)\">",
        "<img src=\"data:image/png;base64,AAAA\">",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"java\tscript:alert(1)\">x</a>",
        "<a href=\"java&#09;script:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"&#x6A;&#x61;&#x76;ascript:alert(1)\">x</a>",
        "<a href=\"&#0000106&#0000097&#0000118&#0000097&#0000115&#0000099&#0000114&#0000105&#0000112&#0000116&#0000058alert(1)\">x</a>",
        "<a href=\" javascript:alert(1)\">x</a>",
        "<a href=\"\u0001javascript:alert(1)\">x</a>",
        "<a href=\"data:text/html,<script>alert(1)</script>\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<a href=\"https://x.example\" onclick=\"alert(1)\">x</a>",
        "<a href=\"https://x.example\" onmouseover=alert(1)>x</a>",
        "<svg onload=alert(1)>",
        "<svg><script>alert(1)</script></svg>",
        "<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>",
        "<math><mtext><script>alert(1)</script></mtext></math>",
        "<p style=\"background:url(javascript:alert(1))\">x</p>",
        "<p style=\"width: expression(alert(1))\">x</p>",
        "<p STYLE=\"color:red\">x</p>",
        "<div onmouseover=\"alert(1)\">x</div>",
        "<DIV ONCLICK=alert(1)>x</DIV>",
        "<body onload=alert(1)>x",
        "<iframe src=\"javascript:alert(1)\"></iframe>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<object data=\"x.swf\"></object>",
        "<embed src=x>",
        "<form action=\"https://evil.example\"><input name=x><button formaction=\"javascript:alert(1)\">go</button></form>",
        "<p><scr<script>ipt>alert(1)</scr</script>ipt></p>",
        "<<script>alert(1)//<</script>",
        "<scr\0ipt>alert(1)</scr\0ipt>",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<link rel=\"stylesheet\" href=\"https://evil.example/x.css\">",
        "<base href=\"//evil.example/\">",
        "<style>@import 'https://evil.example/x.css'; body{background:url(javascript:alert(1))}</style>",
        "<template><script>alert(1)</script></template>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>",
        "<details open ontoggle=alert(1)>x</details>",
        "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">",
        "<!--[if gte IE 4]><script>alert(1)</script><![endif]-->",
        "<![CDATA[<script>alert(1)</script>]]>",
        "<video src=x onerror=alert(1)><source onerror=alert(1)></video>",
        "<audio src=x onerror=alert(1)></audio>",
        "<input type=\"image\" src=x onerror=alert(1)>",
        "<textarea><script>alert(1)</script></textarea>",
        "<select><option onfocus=alert(1)>x</option></select>",
        "<table background=\"javascript:alert(1)\"><tr><td background=\"javascript:alert(1)\">x</td></tr></table>",
        "<a href=\"https://x.example\" style=\"position:fixed;top:0;left:0;width:100%;height:100%\">cover</a>",
        "<p onmouseover=alert(1)//>x</p>",
        "<a href=\"#\" onfocus=alert(1) autofocus>x</a>",
        "&lt;script&gt;alert(1)&lt;/script&gt;<b>ok</b>",
        "<b <script>alert(1)</script>>x</b>",
        "<img/src=x/onerror=alert(1)>",
        "<a href=\"javascript&colon;alert(1)\">x</a>",
        "<a href=\"javascript&#58;alert(1)\">x</a>",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_is_neutralized(string payload)
    {
        var clean = Preview.Sanitize("<p>before</p>" + payload + "<p>after</p>");
        Assert.DoesNotMatch(Danger(), clean);
        Assert.Contains("before", clean);
        Assert.Contains("after", clean);

        // it must stay clean when it is cleaned again, and when it goes through the page renderer
        Assert.Equal(clean, Preview.Sanitize(clean));
        var page = Preview.RenderText(Kit.Rich(1, "x", payload), id => "/Notes/View/" + id);
        Assert.DoesNotMatch(Danger(), page);
    }

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_in_markdown_is_neutralized(string payload)
    {
        var page = Preview.RenderText(Kit.Md(1, "x", "text\n\n" + payload + "\n\n[click](javascript:alert(1)) ![i](x.png) ![j](javascript:alert(1))"), id => "/Notes/View/" + id);
        Assert.DoesNotMatch(Danger(), page);
    }

    [Fact]
    public void The_editor_subset_is_kept()
    {
        const string html = "<h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4><p>a <strong>b</strong> <b>b2</b> <em>e</em> <i>i</i> <u>u</u> <s>s</s> <strike>st</strike> <del>d</del><br>x</p>" +
            "<div><span>sp</span></div><ul><li>1</li></ul><ol><li>2</li></ol><blockquote>q</blockquote><pre><code>c</code></pre><hr>" +
            "<table><thead><tr><th>h</th></tr></thead><tbody><tr><td>d</td></tr></tbody></table>";
        var clean = Preview.Sanitize(html);
        foreach (var tag in new[] { "h1", "h2", "h3", "h4", "p", "strong", "b", "em", "i", "u", "s", "strike", "del", "br", "div", "span", "ul", "ol", "li", "blockquote", "pre", "code", "hr", "table", "thead", "tbody", "tr", "th", "td" })
        {
            Assert.Contains("<" + tag, clean);
        }
    }

    [Fact]
    public void Links_keep_only_safe_addresses()
    {
        Assert.Contains("href=\"https://example.org/a?b=1&amp;c=2\"", Preview.Sanitize("<a href=\"https://example.org/a?b=1&c=2\">x</a>"));
        Assert.Contains("href=\"http://example.org\"", Preview.Sanitize("<a href=\"http://example.org\">x</a>"));
        Assert.Contains("href=\"mailto:me@example.org\"", Preview.Sanitize("<a href=\"mailto:me@example.org\">x</a>"));
        Assert.Contains("href=\"/Notes/View/42\"", Preview.Sanitize("<a href=\"/Notes/View/42\">x</a>"));
        Assert.Contains("href=\"/s/abc-123/n/42\"", Preview.Sanitize("<a href=\"/s/abc-123/n/42\">x</a>"));
        foreach (var bad in new[] { "//evil.example/x", "/\\evil.example", "/Admin/Users", "/Notes/Edit/1", "ftp://x.example/f", "file:///etc/passwd", "tel:123", "relative/path", "#frag", "\\\\evil\\share" })
        {
            var clean = Preview.Sanitize($"<a href=\"{bad}\">x</a>");
            Assert.DoesNotContain("href", clean);
            Assert.Contains(">x</a>", clean);
        }
    }

    [Fact]
    public void External_links_get_rel_and_never_a_target()
    {
        var clean = Preview.Sanitize("<a href=\"https://example.org\" target=\"_blank\" rel=\"opener\">x</a>");
        Assert.Contains("rel=\"noopener noreferrer\"", clean);
        Assert.DoesNotContain("target", clean);
        Assert.DoesNotContain("opener\"", clean.Replace("noopener noreferrer", string.Empty));
        var inner = Preview.Sanitize("<a href=\"/Notes/View/1\" target=\"_blank\" rel=\"x\">x</a>");
        Assert.DoesNotContain("target", inner);
        Assert.DoesNotContain("rel=", inner);
    }

    [Fact]
    public void Class_is_only_the_note_link_class()
    {
        var clean = Preview.Sanitize("<a class=\"mn-note-link evil\" data-note-id=\"42\" href=\"/Notes/View/42\">x</a><p class=\"mat-btn\">y</p><span class=\"mn-note-link\">z</span>");
        Assert.Contains("class=\"mn-note-link\"", clean);
        Assert.DoesNotContain("evil", clean);
        Assert.DoesNotContain("mat-btn", clean);
        Assert.Contains("<p>y</p>", clean);
    }

    [Theory]
    [InlineData("42", true)]
    [InlineData("0042", true)]
    [InlineData("42abc", false)]
    [InlineData("-1", false)]
    [InlineData("1 2", false)]
    [InlineData("1' onclick='alert(1)", false)]
    [InlineData("", false)]
    [InlineData("4٣", false)]
    public void Note_id_is_digits_only(string id, bool kept)
    {
        var clean = Preview.Sanitize($"<a class=\"mn-note-link\" data-note-id=\"{id}\" href=\"/Notes/View/1\">x</a>");
        Assert.Equal(kept, clean.Contains("data-note-id"));
    }

    [Fact]
    public void Other_attributes_are_removed()
    {
        var clean = Preview.Sanitize("<p id=\"a\" title=\"t\" data-foo=\"bar\" aria-label=\"z\" lang=\"de\" contenteditable=\"true\" draggable=\"true\" name=\"n\">x</p>");
        Assert.Equal("<p>x</p>", clean);
    }

    [Fact]
    public void Empty_and_whitespace_input_gives_empty_output()
    {
        Assert.Equal(string.Empty, Preview.Sanitize(string.Empty));
        Assert.Equal(string.Empty, Preview.Sanitize("   "));
        Assert.Equal(string.Empty, Preview.Sanitize(null!));
    }

    [Fact]
    public void Text_and_umlauts_are_not_encoded()
    {
        var clean = Preview.Sanitize("<p>Ärger öffentlich € " + Kit.Cjk + " 😀 &amp; &lt;b&gt;</p>");
        Assert.Contains("Ärger öffentlich € " + Kit.Cjk + " 😀 &amp; &lt;b&gt;", clean);
    }

    [Fact]
    public void Deeply_nested_markup_does_not_break_it()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 3000)) + "<script>alert(1)</script>deep" + string.Concat(Enumerable.Repeat("</div>", 3000));
        var clean = Preview.Sanitize(html);
        Assert.DoesNotContain("script", clean);
        Assert.Contains("deep", clean);
    }

    [Fact]
    public void Sanitizing_in_parallel_is_safe()
    {
        var results = new string[64];
        Parallel.For(0, results.Length, i => results[i] = Preview.Sanitize($"<p onclick=\"x\">n{i}<script>x</script><a href=\"javascript:1\">l</a></p>"));
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal($"<p>n{i}<a>l</a></p>", results[i]);
        }
    }
}

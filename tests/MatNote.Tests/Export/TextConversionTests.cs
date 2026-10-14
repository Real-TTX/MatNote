using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

public class TextConversionTests
{
    private const string Sample =
        "<h1>Titel &amp; Co</h1><p>Hallo <strong>Welt</strong> und <em>Mond</em>, <u>unter</u> <s>weg</s>.<br>Neue Zeile mit Ärger öffentlich.</p>" +
        "<ul><li>Eins</li><li>Zwei<ul><li>Zwei-a</li></ul></li></ul><ol><li>Erst</li><li>Dann</li></ol>" +
        "<blockquote><p>Zitat</p></blockquote><pre>code line 1\n  code line 2</pre><hr>" +
        "<table><tr><th>Name</th><th>Wert</th></tr><tr><td>a</td><td>1</td></tr></table>" +
        "<p><a href=\"https://example.com/x?a=1&amp;b=2\">Link</a></p>";

    private static Note Rich(string html = Sample) => Kit.Rich(1, "Meine Notiz", html);

    [Fact]
    public async Task Rich_to_txt_is_readable()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Txt);
        var t = Kit.Utf8(r);
        Assert.Equal("Meine-Notiz.txt".Replace('-', ' '), r.FileName);
        Assert.Equal("text/plain; charset=utf-8", r.ContentType);
        Assert.StartsWith("Meine Notiz\n===========\n\n", t);
        Assert.Contains("Titel & Co\n==========", t);
        Assert.Contains("Hallo Welt und Mond, unter weg.\nNeue Zeile mit Ärger öffentlich.", t);
        Assert.Contains("- Eins\n- Zwei\n  - Zwei-a", t);
        Assert.Contains("1. Erst\n2. Dann", t);
        Assert.Contains("> Zitat", t);
        Assert.Contains("    code line 1\n      code line 2", t);
        Assert.Contains("Name | Wert\na | 1", t);
        Assert.Contains("Link (https://example.com/x?a=1&b=2)", t);
        Assert.DoesNotContain("<", t);
        Assert.False(r.Data.Length >= 3 && r.Data[0] == 0xEF, "txt has no BOM");
    }

    [Fact]
    public async Task Rich_to_markdown_keeps_the_formatting()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Markdown);
        var t = Kit.Utf8(r);
        Assert.Equal("Meine Notiz.md", r.FileName);
        Assert.Equal("text/markdown; charset=utf-8", r.ContentType);
        Assert.StartsWith("# Meine Notiz\n\n# Titel & Co\n", t);
        Assert.Contains("Hallo **Welt** und *Mond*, <u>unter</u> ~~weg~~.", t);
        Assert.Contains("- Eins\n- Zwei\n  - Zwei-a", t);
        Assert.Contains("1. Erst\n2. Dann", t);
        Assert.Contains("> Zitat", t);
        Assert.Contains("```\ncode line 1\n  code line 2\n```", t);
        Assert.Contains("| Name | Wert |\n| --- | --- |\n| a | 1 |", t);
        Assert.Contains("[Link](https://example.com/x?a=1&b=2)", t);
    }

    [Fact]
    public async Task Markdown_output_renders_back_to_the_same_structure()
    {
        // the markdown we write must be markdown for a real parser: render it again and look at the HTML
        var md = Kit.Utf8(await Kit.Export(Rich(), ExportFormat.Markdown));
        var html = Kit.Preview().RenderText(Kit.Md(2, "x", md), id => "/n/" + id);
        Assert.Contains("<strong>Welt</strong>", html);
        Assert.Contains("<em>Mond</em>", html);
        Assert.Contains("<li>Eins", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("href=\"https://example.com/x?a=1&amp;b=2\"", html);
    }

    [Fact]
    public async Task Rich_to_html_is_a_complete_safe_document()
    {
        var r = await Kit.Export(Rich("<p onclick=\"x()\">a<script>alert(1)</script><a href=\"javascript:alert(1)\">bad</a> <b>ok</b></p>"), ExportFormat.Html);
        var t = Kit.Utf8(r);
        Assert.Equal("text/html; charset=utf-8", r.ContentType);
        Assert.StartsWith("<!DOCTYPE html>", t);
        Assert.Contains("<meta charset=\"utf-8\">", t);
        Assert.Contains("<title>Meine Notiz</title>", t);
        Assert.Contains("<h1>Meine Notiz</h1>", t);
        Assert.Contains("<strong>ok</strong>", t);
        Assert.DoesNotContain("alert", t);
        Assert.DoesNotContain("javascript", t);
        Assert.DoesNotContain("onclick", t);
        Assert.Contains("bad", t);
    }

    [Fact]
    public async Task Html_export_of_the_sample_has_all_structure()
    {
        var t = Kit.Utf8(await Kit.Export(Rich(), ExportFormat.Html));
        Assert.Contains("<h1>Titel &amp; Co</h1>", t);
        Assert.Contains("<ul>", t);
        Assert.Contains("<ol>", t);
        Assert.Contains("<blockquote>", t);
        Assert.Contains("<pre><code>code line 1\n  code line 2</code></pre>", t);
        Assert.Contains("<th>Name</th>", t);
        Assert.Contains("<a href=\"https://example.com/x?a=1&amp;b=2\" rel=\"noopener noreferrer\">Link</a>", t);
        Assert.Contains("Ärger öffentlich", t);
    }

    [Fact]
    public async Task Special_characters_survive_every_text_format()
    {
        var note = Kit.Rich(1, "S", "<p>" + System.Net.WebUtility.HtmlEncode(Kit.Specials + " " + Kit.Umlauts + " " + Kit.Cjk + " 😀") + "</p>");
        foreach (var format in new[] { ExportFormat.Txt, ExportFormat.Html, ExportFormat.Markdown })
        {
            var t = Kit.Utf8(await Kit.Export(note, format));
            Assert.Contains(Kit.Umlauts, t);
            Assert.Contains(Kit.Cjk, t);
            Assert.Contains("😀", t);
        }

        Assert.Contains(Kit.Specials, Kit.Utf8(await Kit.Export(note, ExportFormat.Txt)));
        Assert.Contains("a &amp; b &lt; c &gt; d \\ e { f } g", Kit.Utf8(await Kit.Export(note, ExportFormat.Html)));
        Assert.Contains("a & b \\< c \\> d \\\\ e { f } g", Kit.Utf8(await Kit.Export(note, ExportFormat.Markdown)));
    }

    [Fact]
    public async Task Markdown_note_exports_as_the_markdown_source()
    {
        const string md = "# Überschrift\n\nText mit **fett** und `code`.\n\n- a\n- b\n\n```\nx < y\n```\n";
        var r = await Kit.Export(Kit.Md(1, "Doc", md), ExportFormat.Markdown);
        var t = Kit.Utf8(r);
        Assert.Contains("# Überschrift", t);
        Assert.Contains("Text mit **fett** und `code`.", t);
        Assert.Contains("```\nx < y\n```", t);
        Assert.Equal("Doc.md", r.FileName);
    }

    [Fact]
    public async Task Markdown_note_to_html_and_txt_goes_through_a_real_parser()
    {
        const string md = "# Head\n\nA **bold** [link](https://example.org) and <script>alert(1)</script>\n\n| h1 | h2 |\n|----|----|\n| c1 | c2 |\n\n1. one\n2. two\n";
        var html = Kit.Utf8(await Kit.Export(Kit.Md(1, "M", md), ExportFormat.Html));
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("href=\"https://example.org\"", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("alert", html);
        Assert.Contains("<th>h1</th>", html);
        Assert.Contains("<ol>", html);
        var txt = Kit.Utf8(await Kit.Export(Kit.Md(1, "M", md), ExportFormat.Txt));
        Assert.Contains("A bold link (https://example.org)", txt);
        Assert.Contains("h1 | h2\nc1 | c2", txt);
        Assert.Contains("1. one\n2. two", txt);
    }

    [Fact]
    public async Task Plain_text_keeps_its_lines_in_every_format()
    {
        var note = Kit.Txt(1, "Plain", "Zeile 1\nZeile 2 & <b>\n\nAbsatz 2 ö");
        var txt = Kit.Utf8(await Kit.Export(note, ExportFormat.Txt));
        Assert.Contains("Zeile 1\nZeile 2 & <b>\n\nAbsatz 2 ö", txt);
        var html = Kit.Utf8(await Kit.Export(note, ExportFormat.Html));
        Assert.Contains("<p>Zeile 1<br>Zeile 2 &amp; &lt;b&gt;</p>", html);
        var md = Kit.Utf8(await Kit.Export(note, ExportFormat.Markdown));
        Assert.Contains("Zeile 1  \nZeile 2 & \\<b\\>\n\nAbsatz 2 ö", md);
    }

    [Fact]
    public async Task Title_is_not_repeated_when_the_content_starts_with_it()
    {
        var note = Kit.Rich(1, "Plan", "<h1>Plan</h1><p>x</p>");
        Assert.StartsWith("Plan\n====", Kit.Utf8(await Kit.Export(note, ExportFormat.Txt)));
        var md = Kit.Utf8(await Kit.Export(note, ExportFormat.Markdown));
        Assert.StartsWith("# Plan\n\nx", md);
        var mdNote = Kit.Md(2, "Plan", "# Plan\n\ntext");
        Assert.StartsWith("# Plan\n\ntext", Kit.Utf8(await Kit.Export(mdNote, ExportFormat.Markdown)));
    }

    [Fact]
    public async Task Editor_style_attributes_and_div_lines_are_understood()
    {
        var note = Kit.Rich(1, "T", "<div>eins <span style=\"font-weight: bold\">fett</span></div><div>zwei<br></div><div><br></div><div>drei&nbsp;&nbsp;x</div>");
        var md = Kit.Utf8(await Kit.Export(note, ExportFormat.Markdown));
        Assert.Contains("eins **fett**\n\nzwei\n\ndrei", md);
        var txt = Kit.Utf8(await Kit.Export(note, ExportFormat.Txt));
        Assert.Contains("eins fett\n\nzwei\n\ndrei", txt);
    }

    [Fact]
    public async Task Empty_note_exports_without_error()
    {
        foreach (var format in new[] { ExportFormat.Txt, ExportFormat.Markdown, ExportFormat.Html, ExportFormat.Rtf, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Json })
        {
            var r = await Kit.Export(Kit.Rich(1, string.Empty, string.Empty), format);
            Assert.NotEmpty(r.Data);
            Assert.StartsWith("note", r.FileName);
        }
    }

    [Theory]
    [InlineData("../../etc/passwd", "etc passwd")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a b c d e f g h i j")]
    [InlineData("CON", "CON_")]
    [InlineData("  ...  ", "note")]
    [InlineData("Bericht 2026-10", "Bericht 2026-10")]
    public async Task File_name_is_safe(string title, string expectedStem)
    {
        var r = await Kit.Export(Kit.Rich(1, title, "<p>x</p>"), ExportFormat.Txt);
        Assert.Equal(expectedStem + ".txt", r.FileName);
        Assert.DoesNotContain(r.FileName, c => "/\\:*?\"<>|".Contains(c));
    }

    [Fact]
    public async Task Long_title_is_cut()
    {
        var r = await Kit.Export(Kit.Rich(1, new string('x', 500), "<p>x</p>"), ExportFormat.Pdf);
        Assert.True(r.FileName.Length <= 104, r.FileName);
        Assert.EndsWith(".pdf", r.FileName);
    }
}

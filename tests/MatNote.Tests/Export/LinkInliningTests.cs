using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>"When a note is exported, the linked notes are copied into it": position, depth, cycles, duplicates, formats, and never a note the person cannot see.</summary>
public class LinkInliningTests
{
    private static string L(long id, string title) => Kit.Link(id, title);

    private static async Task<string> Html(Note note, NoteResolver resolver, ExportOptions? options = null) =>
        Kit.Utf8(await Kit.Export(note, ExportFormat.Html, options ?? Kit.Inline, resolver));

    private static async Task<string> Txt(Note note, NoteResolver resolver, ExportOptions? options = null) =>
        Kit.Utf8(await Kit.Export(note, ExportFormat.Txt, options ?? Kit.Inline, resolver));

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [Fact]
    public async Task Linked_note_is_put_after_the_paragraph_with_the_link()
    {
        var a = Kit.Rich(1, "A", $"<p>vor {L(2, "Zweite")} nach</p><p>naechster Absatz</p>");
        var b = Kit.Rich(2, "Zweite Notiz", "<p>Inhalt von B ä</p>");
        var html = await Html(a, Kit.Resolver(b));
        Assert.Contains("<p>vor Zweite nach</p>\n<section class=\"mn-linked-note\">\n<h2>Zweite Notiz</h2>\n<p>Inhalt von B ä</p>\n</section>\n<p>naechster Absatz</p>", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("data-note-id", html);
        Assert.DoesNotContain("/Notes/View", html);
    }

    [Fact]
    public async Task Text_export_uses_a_ruled_block()
    {
        var a = Kit.Rich(1, "A", $"<p>vor {L(2, "Zweite")} nach</p><p>danach</p>");
        var b = Kit.Rich(2, "Zweite Notiz", "<p>Inhalt B</p>");
        var t = await Txt(a, Kit.Resolver(b));
        Assert.Contains("vor Zweite nach\n\n===== Zweite Notiz =====\n\nInhalt B\n\n=====\n\ndanach", t);
    }

    [Fact]
    public async Task Markdown_export_of_a_markdown_note_inlines_the_source_of_the_linked_note()
    {
        var a = Kit.Md(1, "A", "Erste Zeile mit [Zweite](note:2) im Text.\n\nDanach.");
        var b = Kit.Md(2, "Zweite Notiz", "## Teil\n\nB mit **fett**");
        var md = Kit.Utf8(await Kit.Export(a, ExportFormat.Markdown, Kit.Inline, Kit.Resolver(b)));
        Assert.Contains("Erste Zeile mit Zweite im Text.\n\n---\n\n## Zweite Notiz\n\n## Teil\n\nB mit **fett**\n\n---\n\nDanach.", md);
        Assert.DoesNotContain("note:", md);
    }

    [Fact]
    public async Task Linked_note_is_converted_to_the_target_format()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(2, "Md")} und {L(3, "Pl")}</p>");
        var md = Kit.Md(2, "Markdown Note", "Text mit **fett** und ein [Link](https://example.org)\n\n- eins\n- zwei");
        var pl = Kit.Txt(3, "Plain Note", "Zeile a\nZeile b <x>");
        var html = await Html(a, Kit.Resolver(md, pl));
        Assert.Contains("<h2>Markdown Note</h2>", html);
        Assert.Contains("<strong>fett</strong>", html);
        Assert.Contains("<li>eins</li>", html);
        Assert.Contains("<h2>Plain Note</h2>\n<p>Zeile a<br>Zeile b &lt;x&gt;</p>", html);

        var txt = await Txt(a, Kit.Resolver(md, pl));
        Assert.Contains("Text mit fett und ein Link (https://example.org)", txt);
        Assert.Contains("- eins\n- zwei", txt);

        var docx = await Kit.Export(a, ExportFormat.Docx, Kit.Inline, Kit.Resolver(md, pl));
        Assert.Empty(Kit.Validate(docx.Data));
        var docText = Kit.DocxText(docx.Data);
        Assert.Contains("Markdown Note", docText);
        Assert.Contains("Textmitfettundein", Kit.Squash(docText));
        Assert.Contains("Zeile b <x>", docText);

        var pdf = await Kit.Export(a, ExportFormat.Pdf, Kit.Inline, Kit.Resolver(md, pl));
        var pdfText = Kit.Squash(Kit.PdfText(pdf.Data));
        Assert.Contains("MarkdownNote", pdfText);
        Assert.Contains("PlainNote", pdfText);
        Assert.Contains("Textmitfett", pdfText);

        var rtf = System.Text.Encoding.ASCII.GetString((await Kit.Export(a, ExportFormat.Rtf, Kit.Inline, Kit.Resolver(md, pl))).Data);
        Assert.Contains("Plain Note", rtf);
    }

    [Fact]
    public async Task Markdown_to_rich_and_plain_links_work_in_html()
    {
        var a = Kit.Md(1, "A", "Siehe [Zweite](note:2).");
        var b = Kit.Rich(2, "Zwei", "<p><b>fett</b></p>");
        var html = await Html(a, Kit.Resolver(b));
        Assert.Contains("<p>Siehe Zweite.</p>\n<section class=\"mn-linked-note\">\n<h2>Zwei</h2>\n<p><strong>fett</strong></p>", html);

        var p = Kit.Txt(3, "P", "Siehe [Zweite](note:2).");
        var html2 = await Html(p, Kit.Resolver(b));
        Assert.Contains("<h2>Zwei</h2>", html2);
        Assert.DoesNotContain("note:", html2);
    }

    [Fact]
    public async Task Plain_text_puts_the_block_directly_after_the_line()
    {
        var a = Kit.Txt(1, "A", "Zeile 1\nSiehe [Zwei](note:2) hier\nZeile 3\n\nAbsatz 2");
        var b = Kit.Txt(2, "Zwei", "Inhalt B");
        var t = await Txt(a, Kit.Resolver(b));
        Assert.Contains("Zeile 1\nSiehe Zwei hier\n\n===== Zwei =====\n\nInhalt B\n\n=====\n\nZeile 3\n\nAbsatz 2", t);
    }

    [Fact]
    public async Task Plain_text_without_inclusion_keeps_only_the_link_text()
    {
        var a = Kit.Txt(1, "A", "Siehe [Zwei](note:2) hier\nZeile 2");
        var calls = 0;
        NoteResolver counting = id => { calls++; return Task.FromResult<Note?>(null); };
        var t = await Txt(a, counting, Kit.Plain);
        Assert.Contains("Siehe Zwei hier\nZeile 2", t);
        Assert.DoesNotContain("note:", t);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Links_are_followed_up_to_the_depth_and_cycles_are_cut()
    {
        var a = Kit.Rich(1, "AAA", $"<p>{L(2, "to B")}</p>");
        var b = Kit.Rich(2, "BBB", $"<p>B text {L(3, "to C")} {L(1, "back to A")}</p>");
        var c = Kit.Rich(3, "CCC", $"<p>C text {L(4, "to D")} {L(2, "back to B")} {L(3, "self")}</p>");
        var d = Kit.Rich(4, "DDD", "<p>D text</p>");
        var all = Kit.Resolver(a, b, c, d);

        var html = await Html(a, all, new ExportOptions(true, 3));
        Assert.Equal(1, Count(html, "<h2>BBB</h2>"));
        Assert.Equal(1, Count(html, "<h3>CCC</h3>"));
        Assert.Equal(1, Count(html, "<h4>DDD</h4>"));
        Assert.DoesNotContain("<h2>AAA</h2>", html); // the note itself is not copied into itself
        Assert.Contains("back to A", html);
        Assert.DoesNotContain("<a ", html);

        var two = await Html(a, all, new ExportOptions(true, 2));
        Assert.Contains("<h3>CCC</h3>", two);
        Assert.DoesNotContain("DDD", two);
        Assert.DoesNotContain("D text", two);
        Assert.Contains("to D", two);

        var one = await Html(a, all, new ExportOptions(true, 1));
        Assert.Contains("BBB", one);
        Assert.DoesNotContain("CCC", one.Replace("to C", string.Empty).Replace("C text", string.Empty).Replace("back to C", string.Empty));

        var zero = await Html(a, all, new ExportOptions(true, 0));
        Assert.DoesNotContain("BBB", zero);
        Assert.Contains("to B", zero);
    }

    [Fact]
    public async Task The_page_view_cuts_cycles_too_and_the_depth_is_capped()
    {
        // A -> B -> C -> A, and a long chain 10 -> 11 -> ... -> 30: nothing is rendered twice, nothing runs away
        var a = Kit.Rich(1, "AAA", $"<p>{L(2, "to B")} {L(1, "me")}</p>");
        var b = Kit.Rich(2, "BBB", $"<p>{L(3, "to C")}</p>");
        var c = Kit.Rich(3, "CCC", $"<p>{L(1, "back to A")} {L(2, "back to B")}</p>");
        var notes = new List<Note> { a, b, c };
        for (var i = 10; i < 30; i++)
        {
            notes.Add(Kit.Rich(i, "N" + i, $"<p>{L(i + 1, "next")}</p>"));
        }

        var resolver = Kit.Resolver([.. notes]);
        var html = await Kit.Preview().RenderWithLinkedAsync(a, resolver, id => $"/Notes/View?id={id}");
        Assert.Equal(1, Count(html, ">BBB</a>"));
        Assert.Equal(1, Count(html, ">CCC</a>"));
        Assert.DoesNotContain(">AAA</a>", html);
        Assert.Contains("back to A", html);

        var chain = await Kit.Preview().RenderWithLinkedAsync(notes.First(n => n.Id == 10), resolver, id => $"/Notes/View?id={id}", default);
        Assert.Equal(3, Count(chain, "<section"));
        Assert.Equal(3, Count(chain, "</section>"));
        Assert.DoesNotContain(">N14<", chain);

        // a larger depth than the code allows is cut at the limit
        var deep = Kit.Utf8(await Kit.Export(notes.First(n => n.Id == 10), ExportFormat.Html, new ExportOptions(true, 1000), resolver));
        Assert.Equal(8, Count(deep, "<section"));
    }

    [Fact]
    public async Task A_note_that_links_to_itself_is_not_included()
    {
        var a = Kit.Rich(1, "AAA", $"<p>{L(1, "me")}</p>");
        var t = await Txt(a, Kit.Resolver(a));
        Assert.DoesNotContain("=====", t);
        Assert.Contains("me", t);
    }

    [Fact]
    public async Task Duplicate_links_include_the_note_once()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(2, "x")} and {L(2, "y")}</p><p>{L(2, "z")}</p><p>{L(3, "to C")}</p>");
        var b = Kit.Rich(2, "BBB", "<p>b</p>");
        var c = Kit.Rich(3, "CCC", $"<p>{L(2, "B again")}</p>");
        var html = await Html(a, Kit.Resolver(b, c));
        Assert.Equal(1, Count(html, "BBB"));
        Assert.Equal(1, Count(html, "CCC"));
        Assert.Contains("<p>x and y</p>", html);
        Assert.Contains("<p>z</p>", html);
    }

    [Fact]
    public async Task A_note_the_person_cannot_read_is_never_copied()
    {
        var a = Kit.Rich(1, "A", $"<p>vor {L(2, "Geheim")} nach {L(3, "Offen")}</p>");
        var secret = Kit.Rich(2, "SECRET TITLE", "<p>SECRET CONTENT</p>");
        var open = Kit.Rich(3, "Offen Titel", "<p>offen</p>");
        var asked = new List<long>();
        NoteResolver resolver = id =>
        {
            asked.Add(id);
            return Task.FromResult(id == 2 ? null : open); // 2: the person may not read it
        };

        foreach (var format in new[] { ExportFormat.Txt, ExportFormat.Html, ExportFormat.Markdown, ExportFormat.Rtf, ExportFormat.Json })
        {
            asked.Clear();
            var r = await Kit.Export(a, format, Kit.Inline, resolver);
            var text = format == ExportFormat.Rtf ? System.Text.Encoding.ASCII.GetString(r.Data) : Kit.Utf8(r);
            Assert.DoesNotContain("SECRET", text);
            Assert.Contains(2, asked);
        }

        var t = await Txt(a, resolver);
        Assert.Contains("vor Geheim nach Offen", t);
        Assert.Contains("Offen Titel", t);
        var pdf = Kit.Squash(Kit.PdfText((await Kit.Export(a, ExportFormat.Pdf, Kit.Inline, resolver)).Data));
        Assert.DoesNotContain("SECRET", pdf);
        Assert.Contains("vorGeheimnach", pdf);
        var docx = Kit.DocxText((await Kit.Export(a, ExportFormat.Docx, Kit.Inline, resolver)).Data);
        Assert.DoesNotContain("SECRET", docx);
    }

    [Fact]
    public async Task A_link_to_a_file_note_stays_text()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(2, "Datei")}</p>");
        var file = Kit.Note(2, NoteType.Docx, "File Title");
        var t = await Txt(a, Kit.Resolver(file));
        Assert.DoesNotContain("File Title", t);
        Assert.Contains("Datei", t);
        Assert.DoesNotContain("=====", t);
    }

    [Fact]
    public async Task Links_in_lists_and_tables_are_handled()
    {
        var a = Kit.Rich(1, "A", $"<ul><li>punkt {L(2, "B")}</li><li>zwei</li></ul><table><tr><td>{L(3, "C")}</td><td>x</td></tr></table>");
        var b = Kit.Rich(2, "BBB", "<p>b-inhalt</p>");
        var c = Kit.Rich(3, "CCC", "<p>c-inhalt</p>");
        var t = await Txt(a, Kit.Resolver(b, c));
        // in a list item the block is part of the item (indented); after a table it follows the table
        Assert.Contains("- punkt B\n\n  ===== BBB =====\n\n  b-inhalt\n\n  =====\n- zwei", t);
        Assert.Contains("C | x\n\n===== CCC =====\n\nc-inhalt\n\n=====", t);
        var html = await Html(a, Kit.Resolver(b, c));
        Assert.Contains("<li>\n<p>punkt B</p>\n<section class=\"mn-linked-note\">", html);
        Assert.Contains("</table>\n<section class=\"mn-linked-note\">\n<h2>CCC</h2>", html);
    }

    [Fact]
    public async Task Linked_note_content_is_sanitized_by_construction()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(2, "B")}</p>");
        var b = Kit.Rich(2, "B", "<p onclick=\"x()\">ok</p><script>alert('x')</script><iframe src=\"https://evil\"></iframe>");
        var html = await Html(a, Kit.Resolver(b));
        Assert.Contains("<p>ok</p>", html);
        Assert.DoesNotContain("alert", html);
        Assert.DoesNotContain("iframe", html);
        Assert.DoesNotContain("onclick", html);
    }

    [Fact]
    public async Task Json_lists_the_linked_notes()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(2, "B")}</p>");
        var b = Kit.Rich(2, "BBB", $"<p>b {L(1, "A")} {L(3, "C")}</p>");
        var c = Kit.Rich(3, "CCC", "<p>c</p>");
        var json = Kit.Utf8(await Kit.Export(a, ExportFormat.Json, Kit.Inline, Kit.Resolver(a, b, c)));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var titles = doc.RootElement.GetProperty("linkedNotes").EnumerateArray().Select(e => e.GetProperty("title").GetString()!).ToArray();
        Assert.Equal(["BBB", "CCC"], titles);
        var without = Kit.Utf8(await Kit.Export(a, ExportFormat.Json, Kit.Plain, Kit.Resolver(a, b, c)));
        Assert.DoesNotContain("linkedNotes", without);
    }

    [Fact]
    public async Task Code_in_markdown_is_not_treated_as_a_link()
    {
        var a = Kit.Md(1, "A", "```\n[X](note:2)\n```\n\nText");
        var t = Kit.Utf8(await Kit.Export(a, ExportFormat.Markdown, Kit.Inline, Kit.Resolver(Kit.Md(2, "BBB", "b"))));
        Assert.Contains("[X](note:2)", t);
        Assert.DoesNotContain("BBB", t);
    }

    [Fact]
    public async Task Export_many_inlines_per_note()
    {
        var a = Kit.Rich(1, "A", $"<p>{L(3, "C")}</p>");
        var b = Kit.Rich(2, "B", $"<p>{L(3, "C")}</p>");
        var c = Kit.Rich(3, "CCC", "<p>c</p>");
        var zip = await Kit.Exporter().ExportManyAsync("x", [("", a), ("", b)], ExportFormat.Txt, Kit.Inline, Kit.Resolver(c));
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip.Data));
        foreach (var e in archive.Entries)
        {
            using var reader = new StreamReader(e.Open());
            Assert.Contains("===== CCC =====", reader.ReadToEnd()); // each note gets its own copy: the visited set is per note
        }
    }
}

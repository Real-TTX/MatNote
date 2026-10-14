using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MatNote.Data;
using MatNote.Services.Export;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatNote.Tests.Export;

public class PreviewTests
{
    private static readonly Func<long, string> Url = id => "/Notes/View/" + id;

    private static Note FileNote(long id, NoteType type) => new() { Id = id, Type = type, Title = "f", FileName = "f", CreateDate = DateTime.UtcNow, UpdateDate = DateTime.UtcNow };

    private static (NotePreview Preview, Note Note) Setup(NoteType type, byte[] data, long id = 1)
    {
        var store = new Kit.Store();
        store.Files[id] = data;
        return (Kit.Preview(store), FileNote(id, type));
    }

    // ---------- text notes ----------

    [Fact]
    public void Rich_text_is_sanitized_and_note_links_point_to_the_page_url()
    {
        var note = Kit.Rich(1, "x", "<p>Hi <a class=\"mn-note-link\" data-note-id=\"42\" href=\"/Notes/View/42\">Titel ä</a> <a href=\"javascript:alert(1)\" onclick=\"x()\">bad</a><script>alert(1)</script></p>");
        var html = Kit.Preview().RenderText(note, id => $"/s/tok/n/{id}");
        Assert.Contains("<a class=\"mn-note-link\" data-note-id=\"42\" href=\"/s/tok/n/42\">Titel ä</a>", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("javascript", html);
        Assert.Contains(">bad</a>", html);
    }

    [Fact]
    public void A_note_url_that_is_not_an_address_is_not_used()
    {
        var note = Kit.Rich(1, "x", "<a class=\"mn-note-link\" data-note-id=\"42\" href=\"/Notes/View/42\">T</a>");
        foreach (var bad in new[] { "javascript:alert(1)", "//evil.example/", "data:text/html,x", "" })
        {
            Assert.DoesNotContain("href", Kit.Preview().RenderText(note, _ => bad));
        }
    }

    [Fact]
    public void Markdown_is_rendered_and_note_tokens_become_links()
    {
        var note = Kit.Md(1, "x", "# Kopf\n\nText **fett** mit [Zweite Notiz](note:42) und [extern](https://example.org).\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nAuto https://example.com/x\n\n~~weg~~");
        var html = Kit.Preview().RenderText(note, Url);
        Assert.Contains("<h1>Kopf</h1>", html);
        Assert.Contains("<strong>fett</strong>", html);
        Assert.Contains("<a class=\"mn-note-link\" data-note-id=\"42\" href=\"/Notes/View/42\">Zweite Notiz</a>", html);
        Assert.Contains("href=\"https://example.org\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<a href=\"https://example.com/x\"", html);
        Assert.Contains("<del>weg</del>", html);
        Assert.DoesNotContain("note:", html);
    }

    [Fact]
    public void Markdown_raw_html_is_sanitized()
    {
        var html = Kit.Preview().RenderText(Kit.Md(1, "x", "<b onclick=\"x()\">b</b> <script>alert(1)</script> <img src=x onerror=alert(1)>\n\n<div style=\"x\">d</div>"), Url);
        Assert.Contains("<b>b</b>", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("style", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Plain_text_is_encoded_in_a_pre_with_links()
    {
        var note = Kit.Txt(1, "x", "Zeile 1 <b>&\n  eingerückt [Titel \"q\"](note:7) Ende\n<script>alert(1)</script>");
        var html = Kit.Preview().RenderText(note, Url);
        Assert.StartsWith("<pre class=\"mn-plain\">", html);
        Assert.EndsWith("</pre>", html);
        Assert.Contains("Zeile 1 &lt;b&gt;&amp;\n  eingerückt ", html);
        Assert.Contains("<a class=\"mn-note-link\" data-note-id=\"7\" href=\"/Notes/View/7\">Titel &quot;q&quot;</a> Ende", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("note:", html);
    }

    [Fact]
    public void Render_text_of_a_file_note_is_empty()
    {
        Assert.Equal(string.Empty, Kit.Preview().RenderText(FileNote(1, NoteType.Docx), Url));
    }

    [Fact]
    public void Extract_links_per_type()
    {
        var p = Kit.Preview();
        var rich = Kit.Rich(1, "x", $"<p>{Kit.Link(5, "a")} {Kit.Link(7, "b")} {Kit.Link(5, "again")} <a data-note-id=\"x9\" href=\"/\">no</a> <a href=\"/Notes/View/99\">not marked</a></p>");
        Assert.Equal([5L, 7L], p.ExtractLinks(rich));
        var md = Kit.Md(2, "x", "[a](note:3) text [b](note:11) [c](note:3) [web](https://example.org/note:4) `[x](note:55)`");
        Assert.Equal([3L, 11L, 55L], p.ExtractLinks(md)); // a regex: code is not special here, the pages save what the exporter would find
        var plain = Kit.Txt(3, "x", "see [Title](note:8)\n[Other](note:9) and [Title](note:8)");
        Assert.Equal([8L, 9L], p.ExtractLinks(plain));
        Assert.Empty(p.ExtractLinks(FileNote(4, NoteType.Docx)));
        Assert.Empty(p.ExtractLinks(Kit.Rich(5, "x", string.Empty)));
        Assert.Empty(p.ExtractLinks(Kit.Md(6, "x", "[a](note:99999999999999999999999)")));
    }

    // ---------- DOCX preview ----------

    [Fact]
    public async Task Docx_preview_shows_structure_without_active_content()
    {
        var docx = (await Kit.Export(Kit.Rich(1, "t", "<h1>Überschrift</h1><h2>Zwei</h2><p>Text <strong>fett</strong> <em>kursiv</em> <u>unter</u> <s>weg</s></p><ul><li>eins</li><li>zwei<ul><li>tief</li></ul></li></ul><ol><li>erst</li></ol><table><tr><th>K</th></tr><tr><td>Zelle &amp; <a href=\"https://example.org\">Link</a></td></tr></table>"), ExportFormat.Docx)).Data;
        var (preview, note) = Setup(NoteType.Docx, docx);
        var html = (await preview.RenderFileAsync(note))!;
        Assert.Contains("<h1>Überschrift</h1>", html);
        Assert.Contains("<h2>Zwei</h2>", html);
        Assert.Contains("<strong>fett</strong>", html);
        Assert.Contains("<em>kursiv</em>", html);
        Assert.Contains("<u>unter</u>", html);
        Assert.Contains("<s>weg</s>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>tief</li>", html);
        Assert.Contains("<th><strong>K</strong></th>", html);
        Assert.Contains("Zelle &amp; <a href=\"https://example.org\" rel=\"noopener noreferrer\">Link</a>", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Docx_preview_of_a_hostile_file_has_no_active_links_or_markup()
    {
        var bytes = BuildHostileDocx();
        var (preview, note) = Setup(NoteType.Docx, bytes);
        var html = (await preview.RenderFileAsync(note))!;
        Assert.DoesNotContain("javascript", html);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("onerror", html.Replace("onerror=alert(1)&gt;", string.Empty)); // as text it is allowed, as an attribute not
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains(">click</p>", html);
        Assert.Contains("<h2>Real Heading</h2>", html);
        Assert.Contains("<h1>Der Titel</h1>", html);
        Assert.Contains("<a href=\"mailto:a@b.de\"", html);
    }

    private static byte[] BuildHostileDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new Style(new StyleName { Val = "heading 2" }) { Type = StyleValues.Paragraph, StyleId = "Überschrift2" },
                new Style(new StyleName { Val = "Title" }) { Type = StyleValues.Paragraph, StyleId = "Titel" });
            var js = main.AddHyperlinkRelationship(new Uri("javascript:alert(1)"), true);
            var data = main.AddHyperlinkRelationship(new Uri("data:text/html,<script>alert(1)</script>"), true);
            var mail = main.AddHyperlinkRelationship(new Uri("mailto:a@b.de"), true);
            main.Document = new Document(new Body(
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Titel" }), new Run(new Text("Der Titel"))),
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Überschrift2" }), new Run(new Text("Real Heading"))),
                new Paragraph(new Hyperlink(new Run(new Text("click"))) { Id = js.Id }),
                new Paragraph(new Hyperlink(new Run(new Text("click"))) { Id = data.Id }),
                new Paragraph(new Hyperlink(new Run(new Text("mailto"))) { Id = mail.Id }),
                new Paragraph(new Run(new Text("<script>alert(1)</script> <img src=x onerror=alert(1)>")))));
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task Docx_preview_is_cut_for_a_huge_document()
    {
        var html = string.Concat(Enumerable.Range(0, 20000).Select(i => $"<p>Absatz {i} " + new string('x', 40) + "</p>"));
        var docx = (await Kit.Export(Kit.Rich(1, "t", html), ExportFormat.Docx)).Data;
        var (preview, note) = Setup(NoteType.Docx, docx);
        var result = (await preview.RenderFileAsync(note))!;
        Assert.True(result.Length < 600_000, "length " + result.Length);
        Assert.Contains("The preview is shortened", result);
        Assert.Contains("Absatz 0 ", result);
        Assert.DoesNotContain("Absatz 19999 ", result);
    }

    // ---------- XLSX preview ----------

    [Fact]
    public async Task Xlsx_preview_has_a_table_per_sheet_with_formatted_values()
    {
        var bytes = Kit.Workbook(wb =>
        {
            var ws = wb.AddWorksheet("Umsatz <&>");
            ws.Cell("A1").Value = "Preis";
            ws.Cell("A2").Value = 1234.5;
            ws.Cell("A2").Style.NumberFormat.Format = "#,##0.00 \"EUR\"";
            ws.Cell("B2").Value = new DateTime(2026, 10, 14);
            ws.Cell("B2").Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell("C2").Value = 0.256;
            ws.Cell("C2").Style.NumberFormat.Format = "0.0%";
            ws.Cell("A3").Value = "<img src=x onerror=alert(1)>";
            ws.Range("A4:B4").Merge();
            ws.Cell("A4").Value = "merged äöü";
            wb.AddWorksheet("Leer");
            var hidden = wb.AddWorksheet("Versteckt");
            hidden.Cell("A1").Value = "hidden";
            hidden.Visibility = XLWorksheetVisibility.Hidden;
        });
        var (preview, note) = Setup(NoteType.Xlsx, bytes);
        var html = (await preview.RenderFileAsync(note))!;
        Assert.Contains("<h2>Umsatz &lt;&amp;&gt;</h2>", html);
        Assert.Contains("<td>1,234.50 EUR</td>", html);
        Assert.Contains("<td>14.10.2026</td>", html);
        Assert.Contains("<td>25.6%</td>", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("merged äöü", html);
        Assert.Contains("<h2>Leer</h2>", html);
        Assert.Contains("(empty sheet)", html);
        Assert.DoesNotContain("hidden", html);
    }

    [Fact]
    public async Task Xlsx_preview_is_limited_to_500_rows_and_50_columns()
    {
        var bytes = Kit.Workbook(wb =>
        {
            var ws = wb.AddWorksheet("Big");
            for (var r = 1; r <= 600; r++)
            {
                for (var c = 1; c <= 60; c += 1)
                {
                    ws.Cell(r, c).Value = $"r{r}c{c}x";
                }
            }
        });
        var (preview, note) = Setup(NoteType.Xlsx, bytes);
        var html = (await preview.RenderFileAsync(note))!;
        Assert.Contains("r500c50x", html);
        Assert.DoesNotContain("r501c1x", html);
        Assert.DoesNotContain("r1c51x", html);
        Assert.Contains("Only the first 500 rows and 50 columns are shown.", html);
        Assert.Equal(500, html.Split("<tr>").Length - 1);
    }

    // ---------- other types, damaged files ----------

    [Theory]
    [InlineData(NoteType.RichText)]
    [InlineData(NoteType.PlainText)]
    [InlineData(NoteType.Markdown)]
    [InlineData(NoteType.Pdf)]
    [InlineData(NoteType.File)]
    public async Task Render_file_is_null_for_other_types(NoteType type)
    {
        var (preview, note) = Setup(type, [1, 2, 3]);
        Assert.Null(await preview.RenderFileAsync(note));
    }

    [Theory]
    [InlineData(NoteType.Docx)]
    [InlineData(NoteType.Xlsx)]
    public async Task A_damaged_file_gives_a_message_not_an_exception(NoteType type)
    {
        foreach (var data in new[] { new byte[] { 1, 2, 3, 4 }, Kit.Zip(("a.txt", "no office file")), [], (byte[])[.. new byte[2000].Select((_, i) => (byte)(i * 7))] })
        {
            var (preview, note) = Setup(type, data);
            var html = await preview.RenderFileAsync(note);
            Assert.NotNull(html);
            Assert.StartsWith("<p><em>", html);
            Assert.DoesNotContain("Exception", html);
        }

        var noFile = await Kit.Preview().RenderFileAsync(FileNote(77, type));
        Assert.Contains("no content", noFile);
    }

    [Fact]
    public async Task A_truncated_docx_gives_a_message()
    {
        var docx = (await Kit.Export(Kit.Rich(1, "t", "<p>text</p>"), ExportFormat.Docx)).Data;
        var (preview, note) = Setup(NoteType.Docx, docx[..(docx.Length / 2)]);
        Assert.Contains("could not be created", (await preview.RenderFileAsync(note))!);
    }

    // ---------- search text ----------

    [Fact]
    public async Task Search_text_of_rich_text_has_no_markup_and_block_borders_are_spaces()
    {
        var p = Kit.Preview();
        var note = Kit.Rich(1, "x", "<h1>Titel</h1><p>Eins<br>zwei</p><ul><li>a</li><li>b</li></ul><table><tr><td>c</td><td>d</td></tr></table><p>fa<strong>ll</strong>s &amp; ä</p><script>evil()</script><style>.x{}</style>");
        var text = await p.BuildSearchTextAsync(note, null);
        Assert.Equal("Titel Eins zwei a b c d falls & ä", text);
    }

    [Fact]
    public async Task Search_text_of_markdown_and_plain_text_is_the_text_with_collapsed_whitespace()
    {
        var p = Kit.Preview();
        Assert.Equal("# Kopf Text **fett** ä", await p.BuildSearchTextAsync(Kit.Md(1, "x", "# Kopf\n\n\nText   **fett**\tä\n"), null));
        Assert.Equal("a b c", await p.BuildSearchTextAsync(Kit.Txt(1, "x", " a\r\n b\n\n\n c "), null));
    }

    [Fact]
    public async Task Search_text_is_cut_at_200000_characters()
    {
        var p = Kit.Preview();
        var big = string.Join(' ', Enumerable.Repeat("wort", 100_000));
        Assert.InRange((await p.BuildSearchTextAsync(Kit.Txt(1, "x", big), null)).Length, 199_990, 200_000);
        Assert.True((await p.BuildSearchTextAsync(Kit.Rich(1, "x", "<p>" + big + "</p>"), null)).Length <= 200_000);
    }

    [Fact]
    public async Task Search_text_of_docx_xlsx_and_pdf_is_extracted()
    {
        var docx = (await Kit.Export(Kit.Rich(1, "t", "<h1>Wort1</h1><p>Absatz äöü</p><table><tr><td>Zelle</td></tr></table>"), ExportFormat.Docx)).Data;
        var xlsx = Kit.Workbook(wb =>
        {
            var ws = wb.AddWorksheet("Blatt Eins");
            ws.Cell("A1").Value = "Alpha";
            ws.Cell("C3").Value = 42;
            wb.AddWorksheet("Zwei").Cell("A1").Value = "Beta ä";
        });
        var pdf = (await Kit.Export(Kit.Rich(1, "t", "<p>Hallo PDF Welt äöü</p>"), ExportFormat.Pdf)).Data;
        var p = Kit.Preview();
        var d = await p.BuildSearchTextAsync(FileNote(1, NoteType.Docx), docx);
        Assert.Contains("Wort1", d);
        Assert.Contains("Absatz äöü", d);
        Assert.Contains("Zelle", d);
        var x = await p.BuildSearchTextAsync(FileNote(1, NoteType.Xlsx), xlsx);
        Assert.Contains("Blatt Eins", x);
        Assert.Contains("Alpha", x);
        Assert.Contains("42", x);
        Assert.Contains("Beta ä", x);
        var f = await p.BuildSearchTextAsync(FileNote(1, NoteType.Pdf), pdf);
        Assert.Contains("Hallo PDF Welt äöü", f);
        Assert.DoesNotContain("\n", d + x + f);
    }

    [Fact]
    public async Task Search_text_never_throws()
    {
        var p = Kit.Preview();
        foreach (var type in new[] { NoteType.Docx, NoteType.Xlsx, NoteType.Pdf })
        {
            Assert.Equal(string.Empty, await p.BuildSearchTextAsync(FileNote(1, type), [1, 2, 3, 4, 5]));
            Assert.Equal(string.Empty, await p.BuildSearchTextAsync(FileNote(1, type), []));
            Assert.Equal(string.Empty, await p.BuildSearchTextAsync(FileNote(1, type), null));
            Assert.Equal(string.Empty, await p.BuildSearchTextAsync(FileNote(1, type), Kit.Zip(("x", "y"))));
        }

        Assert.Equal(string.Empty, await p.BuildSearchTextAsync(FileNote(1, NoteType.File), [1, 2, 3]));
        Assert.Equal(string.Empty, await p.BuildSearchTextAsync(Kit.Rich(1, "x", string.Empty), null));
        Assert.Equal(string.Empty, await p.BuildSearchTextAsync(Kit.Rich(1, "x", null!), null));
    }

    [Fact]
    public async Task Search_text_falls_back_to_the_stored_file()
    {
        var pdf = (await Kit.Export(Kit.Rich(1, "t", "<p>Gespeicherter Inhalt</p>"), ExportFormat.Pdf)).Data;
        var (preview, note) = Setup(NoteType.Pdf, pdf);
        Assert.Contains("Gespeicherter Inhalt", await preview.BuildSearchTextAsync(note, null));
    }

    [Fact]
    public void AddExport_registers_the_exporter_and_the_preview()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=localhost;Database=none")); // never opened: nothing is queried
        services.AddExport();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        Assert.IsType<NoteExporter>(scope.ServiceProvider.GetRequiredService<INoteExporter>());
        Assert.IsType<NotePreview>(scope.ServiceProvider.GetRequiredService<INotePreview>());
    }
}

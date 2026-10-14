using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>RTF, Word and PDF: well formed, readable again, the text in them.</summary>
public class DocumentFormatTests
{
    private const string Sample =
        "<h1>Gro\u00DF\u00FCberschrift</h1><h2>Zwei</h2><h3>Drei</h3><h4>Vier</h4>" +
        "<p>Text mit <strong>fett</strong>, <em>kursiv</em>, <u>unterstrichen</u>, <s>durchgestrichen</s> und <code>code()</code>.<br>Zweite Zeile: \u00C4rger \u00F6ffentlich \u00DCberma\u00DF \u20AC.</p>" +
        "<p>Sonderzeichen: a &amp; b &lt; c &gt; d \\ e { f } g</p>" +
        "<ul><li>Eins</li><li>Zwei<ul><li>Zwei-a</li></ul></li></ul><ol><li>Erst</li><li>Dann</li></ol>" +
        "<blockquote><p>Ein Zitat</p></blockquote><pre>int x = 1;\n  return x;</pre><hr>" +
        "<table><tr><th>Name</th><th>Wert</th></tr><tr><td>Alpha</td><td>1</td></tr><tr><td>Beta</td><td>2</td></tr></table>" +
        "<p><a href=\"https://example.com/seite?a=1&amp;b=2\">Beispiel-Link</a></p>";

    private static Note Rich(string html = Sample, string title = "Gr\u00FC\u00DFe & Notiz") => Kit.Rich(1, title, html);

    private static bool Balanced(string rtf)
    {
        var depth = 0;
        for (var i = 0; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++; // the escaped character (or the first letter of a control word) is not a group
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return depth == 0;
    }

    private static string RtfPlainText(string rtf)
    {
        // good enough for a test: decode \uN? escapes and drop control words and braces
        var s = Regex.Replace(rtf, @"\\u(-?\d+)\?", m => ((char)(short)int.Parse(m.Groups[1].Value)).ToString());
        s = s.Replace("\\\\", "\u0001").Replace("\\{", "\u0002").Replace("\\}", "\u0003");
        s = Regex.Replace(s, @"\\[a-z]+-?\d* ?", string.Empty);
        s = s.Replace("{", string.Empty).Replace("}", string.Empty);
        return s.Replace('\u0001', '\\').Replace('\u0002', '{').Replace('\u0003', '}');
    }

    [Fact]
    public async Task Rtf_is_well_formed_and_contains_the_text()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Rtf);
        var rtf = Encoding.ASCII.GetString(r.Data);
        Assert.Equal("application/rtf", r.ContentType);
        Assert.EndsWith(".rtf", r.FileName);
        Assert.StartsWith("{\\rtf1\\ansi", rtf);
        Assert.True(Balanced(rtf), "braces are balanced");
        Assert.All(r.Data, b => Assert.True(b < 128, "RTF is 7 bit"));
        var text = RtfPlainText(rtf);
        Assert.Contains("Gro\u00DF\u00FCberschrift", text);
        Assert.Contains("Zweite Zeile: \u00C4rger \u00F6ffentlich \u00DCberma\u00DF \u20AC.", text.Replace("\n", " ").Replace("  ", " "));
        Assert.Contains("a & b < c > d \\ e { f } g", text);
        Assert.Contains("Gr\u00FC\u00DFe & Notiz", text);
        Assert.Contains("Beispiel-Link", text);
        Assert.Contains("\\b ", rtf);
        Assert.Contains("\\i ", rtf);
        Assert.Contains("\\ul ", rtf);
        Assert.Contains("\\strike", rtf);
        Assert.Contains("{\\field{\\*\\fldinst{HYPERLINK \"https://example.com/seite?a=1&b=2\"}}", rtf);
        Assert.Contains("\\trowd", rtf);
        Assert.Contains("\\cell", rtf);
        Assert.Contains("\\u8364?", rtf); // the euro sign
        Assert.Contains("\\\\ e \\{ f \\} g", rtf);
    }

    [Fact]
    public async Task Rtf_escapes_cjk_and_emoji_as_utf16_units()
    {
        var r = await Kit.Export(Kit.Rich(1, "T", "<p>" + Kit.Cjk + " \uD83D\uDE00</p>"), ExportFormat.Rtf);
        var rtf = Encoding.ASCII.GetString(r.Data);
        Assert.True(Balanced(rtf));
        Assert.Contains("\\u26085?", rtf); // U+65E5
        Assert.Contains("\\u-10179?\\u-8704?", rtf); // U+1F600 as a surrogate pair, signed
        Assert.Contains(Kit.Cjk, RtfPlainText(rtf));
    }

    [Fact]
    public async Task Docx_is_valid_for_the_open_xml_schema()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Docx);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", r.ContentType);
        Assert.EndsWith(".docx", r.FileName);
        Assert.Equal((byte)'P', r.Data[0]); // a zip
        Assert.Empty(Kit.Validate(r.Data));
    }

    [Fact]
    public async Task Docx_has_the_text_and_real_word_structure()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Docx);
        var text = Kit.DocxText(r.Data);
        foreach (var part in new[] { "Gr\u00FC\u00DFe & Notiz", "Gro\u00DF\u00FCberschrift", "fett", "kursiv", "Zweite Zeile: \u00C4rger \u00F6ffentlich \u00DCberma\u00DF \u20AC.", "a & b < c > d \\ e { f } g", "Zwei-a", "Ein Zitat", "int x = 1;", "Alpha", "Beispiel-Link" })
        {
            Assert.Contains(part, text);
        }

        using var ms = new MemoryStream(r.Data);
        using var doc = WordprocessingDocument.Open(ms, false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;
        var styles = body.Descendants<ParagraphStyleId>().Select(s => s.Val!.Value).ToHashSet();
        foreach (var s in new[] { "Title", "Heading1", "Heading2", "Heading3", "Heading4", "ListParagraph", "Quote", "Code" })
        {
            Assert.Contains(s, styles);
        }

        var defined = main.StyleDefinitionsPart!.Styles!.Elements<Style>().Select(s => s.StyleId!.Value).ToHashSet();
        Assert.All(styles, s => Assert.Contains(s, defined));
        Assert.Contains("Hyperlink", defined);
        Assert.Contains("TableGrid", defined);
        Assert.Single(body.Descendants<Table>());
        Assert.Equal(3, body.Descendants<TableRow>().Count());
        Assert.NotEmpty(body.Descendants<Bold>());
        Assert.NotEmpty(body.Descendants<Italic>());
        Assert.NotEmpty(body.Descendants<Underline>());
        Assert.NotEmpty(body.Descendants<Strike>());
        var link = Assert.Single(body.Descendants<Hyperlink>());
        Assert.Equal("https://example.com/seite?a=1&b=2", main.HyperlinkRelationships.Single(h => h.Id == link.Id!.Value).Uri.ToString());
        Assert.NotEmpty(body.Descendants<NumberingProperties>());
        Assert.NotNull(main.NumberingDefinitionsPart);
    }

    [Fact]
    public async Task Docx_numbering_restarts_for_every_ordered_list()
    {
        var r = await Kit.Export(Rich("<ol><li>a</li></ol><p>x</p><ol><li>b</li></ol>"), ExportFormat.Docx);
        using var ms = new MemoryStream(r.Data);
        using var doc = WordprocessingDocument.Open(ms, false);
        var ids = doc.MainDocumentPart!.Document!.Body!.Descendants<NumberingId>().Select(n => n.Val!.Value).Distinct().ToList();
        Assert.Equal(2, ids.Count);
        Assert.Empty(Kit.Validate(r.Data));
    }

    [Fact]
    public async Task Docx_survives_cjk_emoji_control_characters_and_empty_cells()
    {
        var html = "<p>" + Kit.Cjk + " \uD83D\uDE00 control:\u0001\u0008 end</p><table><tr><td></td><td>x</td></tr><tr><td>y</td></tr></table><p></p><ul><li></li></ul>";
        var r = await Kit.Export(Rich(html), ExportFormat.Docx);
        Assert.Empty(Kit.Validate(r.Data));
        var text = Kit.DocxText(r.Data);
        Assert.Contains(Kit.Cjk + " \uD83D\uDE00 control: end", text);
    }

    [Fact]
    public async Task Docx_of_nested_structure_is_valid()
    {
        const string html = "<ul><li><p>one</p><p>one b</p><ul><li>deep<ol><li>deeper</li></ol></li></ul></li></ul><blockquote><blockquote><p>nested</p></blockquote><ul><li>in quote</li></ul></blockquote>" +
            "<table><tr><td><ul><li>x</li><li>y</li></ul></td><td><a href=\"mailto:a@b.de\">mail</a> <a href=\"javascript:alert(1)\">bad</a></td></tr></table>";
        var r = await Kit.Export(Rich(html), ExportFormat.Docx);
        Assert.Empty(Kit.Validate(r.Data));
        var text = Kit.DocxText(r.Data);
        Assert.Contains("deeper", text);
        Assert.Contains("mail", text);
        Assert.Contains("bad", text);
        using var ms = new MemoryStream(r.Data);
        using var doc = WordprocessingDocument.Open(ms, false);
        Assert.Single(doc.MainDocumentPart!.Document!.Body!.Descendants<Hyperlink>()); // the javascript: link is text only
    }

    [Fact]
    public async Task Markdown_and_plain_notes_make_valid_docx_too()
    {
        var md = await Kit.Export(Kit.Md(1, "M", "# H\n\ntext **b**\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- x\n- y\n"), ExportFormat.Docx);
        Assert.Empty(Kit.Validate(md.Data));
        Assert.Contains("text", Kit.DocxText(md.Data));
        var plain = await Kit.Export(Kit.Txt(2, "P", "line 1\nline 2 \u00E4\n\npara 2"), ExportFormat.Docx);
        Assert.Empty(Kit.Validate(plain.Data));
        Assert.Contains("line 2 \u00E4", Kit.DocxText(plain.Data));
    }

    [Fact]
    public async Task Pdf_is_a_pdf_with_selectable_text()
    {
        var r = await Kit.Export(Rich(), ExportFormat.Pdf);
        Assert.Equal("application/pdf", r.ContentType);
        Assert.EndsWith(".pdf", r.FileName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(r.Data, 0, 4));
        var text = Kit.Squash(Kit.PdfText(r.Data));
        foreach (var part in new[] { "Gr\u00FC\u00DFe&Notiz", "Gro\u00DF\u00FCberschrift", "Textmitfett,kursiv", "Zweite Zeile: \u00C4rger \u00F6ffentlich \u00DCberma\u00DF \u20AC.", "a&b<c>d\\e{f}g", "Zwei-a", "EinZitat", "intx=1;", "Alpha", "Beispiel-Link" })
        {
            Assert.Contains(Kit.Squash(part), text);
        }
    }

    [Fact]
    public async Task Pdf_with_cjk_and_emoji_does_not_crash()
    {
        var r = await Kit.Export(Rich("<p>Latin text before. " + Kit.Cjk + " \uD83D\uDE00 \u0643\u062A\u0627\u0628 \u0394\u03B5\u03BB\u03C4\u03B1 \u0416\u0443\u0440\u043D\u0430\u043B after.</p>"), ExportFormat.Pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(r.Data, 0, 4));
        var text = Kit.Squash(Kit.PdfText(r.Data));
        Assert.Contains("Latintextbefore.", text);
        Assert.Contains("after.", text);
        Assert.Contains("\u0394\u03B5\u03BB\u03C4\u03B1", text); // Greek and Cyrillic are in Liberation and DejaVu
        Assert.Contains("\u0416\u0443\u0440\u043D\u0430\u043B", text);
    }

    [Fact]
    public async Task Pdf_of_a_long_note_has_several_pages_and_does_not_fail()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 120; i++)
        {
            sb.Append("<h2>Abschnitt ").Append(i).Append("</h2><p>").Append(string.Join(' ', Enumerable.Repeat("Lorem ipsum dolor sit amet \u00E4\u00F6\u00FC", 15))).Append("</p>");
        }

        sb.Append("<pre>").Append(string.Join('\n', Enumerable.Range(0, 200).Select(i => "line " + i + " " + new string('x', 150)))).Append("</pre>");
        sb.Append("<table>").Append(string.Concat(Enumerable.Range(0, 80).Select(i => $"<tr><td>r{i}</td><td>{string.Join(' ', Enumerable.Repeat("cell text", 20))}</td><td>x</td></tr>"))).Append("</table>");
        var r = await Kit.Export(Rich(sb.ToString()), ExportFormat.Pdf);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(r.Data);
        Assert.True(pdf.NumberOfPages > 5, "pages: " + pdf.NumberOfPages);
        Assert.Contains("Abschnitt119", Kit.Squash(Kit.PdfText(r.Data)));
    }

    [Fact]
    public async Task Pdf_of_markdown_and_plain_text()
    {
        var md = await Kit.Export(Kit.Md(1, "M", "# Kopf\n\nText **fett** [l](https://example.org)\n\n- x\n- y"), ExportFormat.Pdf);
        Assert.Contains("Textfettl", Kit.Squash(Kit.PdfText(md.Data)));
        var plain = await Kit.Export(Kit.Txt(2, "P", "Zeile eins\nZeile zwei \u00F6\n\nAbsatz"), ExportFormat.Pdf);
        var t = Kit.Squash(Kit.PdfText(plain.Data));
        Assert.Contains("Zeileeins", t);
        Assert.Contains("Zeilezwei\u00F6", t);
    }

    [Fact]
    public async Task Json_has_title_type_tags_dates_and_content()
    {
        var store = new Kit.Store();
        store.Tags[1] = ["Arbeit", "\u00DCberblick"];
        var r = await Kit.Export(Rich("<p>Hall\u00F6chen \"quoted\"</p>"), ExportFormat.Json, store: store);
        Assert.Equal("application/json; charset=utf-8", r.ContentType);
        Assert.EndsWith(".json", r.FileName);
        using var json = System.Text.Json.JsonDocument.Parse(Kit.Utf8(r));
        var root = json.RootElement;
        Assert.Equal("Gr\u00FC\u00DFe & Notiz", root.GetProperty("title").GetString());
        Assert.Equal("RichText", root.GetProperty("type").GetString());
        Assert.Equal(["Arbeit", "\u00DCberblick"], root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal("<p>Hall\u00F6chen \"quoted\"</p>", root.GetProperty("content").GetString());
        Assert.StartsWith("2026-01-02T03:04:05", root.GetProperty("created").GetString());
        Assert.StartsWith("2026-02-03T04:05:06", root.GetProperty("updated").GetString());
        Assert.Contains("Hall\u00f6chen", Kit.Utf8(r)); // not \u00f6 escaped
    }
}

/// <summary>Proof that the validator used above is not vacuous: it does reject a broken document.</summary>
public class ValidatorControlTests
{
    [Fact]
    public void The_validator_reports_a_document_with_misordered_properties()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(
                new RunProperties(new Underline { Val = UnderlineValues.Single }, new Bold()), // wrong order: the schema wants b before u
                new Text("x")))));
        }

        Assert.NotEmpty(Kit.Validate(ms.ToArray()));
    }
}

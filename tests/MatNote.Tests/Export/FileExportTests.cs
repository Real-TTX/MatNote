using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>Word / Excel / PDF / other file notes: original, extracted text, CSV, HTML, and the zip of many notes.</summary>
public class FileExportTests
{
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static Note FileNote(long id, NoteType type, string title, string fileName, string contentType) =>
        new() { Id = id, Type = type, Title = title, FileName = fileName, ContentType = contentType, CreateDate = DateTime.UtcNow, UpdateDate = DateTime.UtcNow };

    private static byte[] Sheet() => Kit.Workbook(wb =>
    {
        var ws = wb.AddWorksheet("Umsatz ä");
        ws.Cell("A1").Value = "Name";
        ws.Cell("B1").Value = "Betrag";
        ws.Cell("C1").Value = "Datum";
        ws.Cell("D1").Value = "Notiz";
        ws.Cell("A2").Value = "Müller, \"Hans\"";
        ws.Cell("B2").Value = 1234.5;
        ws.Cell("B2").Style.NumberFormat.Format = "#,##0.00";
        ws.Cell("C2").Value = new DateTime(2026, 10, 14);
        ws.Cell("D2").Value = "zwei\nZeilen, ja";
        ws.Cell("A3").Value = "日本語";
        ws.Cell("B3").Value = 0.1;
        ws.Cell("C3").Value = true;
        ws.Cell("D3").FormulaA1 = "B2*2";
        ws.Cell("A5").Value = "<script>alert(1)</script>";
        var second = wb.AddWorksheet("Zweites");
        second.Cell("A1").Value = "second sheet";
    });

    private static (NoteExporter Exporter, Kit.Store Store) With(long id, byte[] data)
    {
        var store = new Kit.Store();
        store.Files[id] = data;
        return (Kit.Exporter(store), store);
    }

    [Fact]
    public void Formats_per_type()
    {
        var e = Kit.Exporter();
        Assert.Equal([ExportFormat.Html, ExportFormat.Txt, ExportFormat.Markdown, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json], e.FormatsFor(NoteType.RichText));
        Assert.Equal([ExportFormat.Txt, ExportFormat.Markdown, ExportFormat.Html, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json], e.FormatsFor(NoteType.PlainText));
        Assert.Equal([ExportFormat.Markdown, ExportFormat.Txt, ExportFormat.Html, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json], e.FormatsFor(NoteType.Markdown));
        Assert.Equal([ExportFormat.Original, ExportFormat.Txt, ExportFormat.Html], e.FormatsFor(NoteType.Docx));
        Assert.Equal([ExportFormat.Original, ExportFormat.Csv, ExportFormat.Html], e.FormatsFor(NoteType.Xlsx));
        Assert.Equal([ExportFormat.Original, ExportFormat.Txt], e.FormatsFor(NoteType.Pdf));
        Assert.Equal([ExportFormat.Original], e.FormatsFor(NoteType.File));
    }

    [Fact]
    public async Task Every_listed_format_can_be_exported()
    {
        var store = new Kit.Store();
        var docx = (await Kit.Export(Kit.Rich(9, "d", "<p>word text</p>"), ExportFormat.Docx)).Data;
        var pdf = (await Kit.Export(Kit.Rich(9, "d", "<p>pdf text</p>"), ExportFormat.Pdf)).Data;
        store.Files[10] = docx;
        store.Files[11] = Sheet();
        store.Files[12] = pdf;
        store.Files[13] = [1, 2, 3];
        var notes = new[]
        {
            Kit.Rich(1, "r", "<p>x</p>"), Kit.Txt(2, "p", "x"), Kit.Md(3, "m", "x"),
            FileNote(10, NoteType.Docx, "w", "w.docx", DocxType), FileNote(11, NoteType.Xlsx, "x", "x.xlsx", XlsxType),
            FileNote(12, NoteType.Pdf, "f", "f.pdf", "application/pdf"), FileNote(13, NoteType.File, "o", "o.bin", "application/octet-stream"),
        };
        var exporter = Kit.Exporter(store);
        foreach (var note in notes)
        {
            foreach (var format in exporter.FormatsFor(note.Type))
            {
                var r = await exporter.ExportAsync(note, format, Kit.Plain, Kit.Nothing);
                Assert.NotEmpty(r.Data);
                Assert.False(string.IsNullOrWhiteSpace(r.FileName));
                Assert.False(string.IsNullOrWhiteSpace(r.ContentType));
            }
        }
    }

    [Fact]
    public async Task Unsupported_format_is_refused()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Kit.Export(Kit.Rich(1, "x", "<p>x</p>"), ExportFormat.Csv));
        await Assert.ThrowsAsync<NotSupportedException>(() => Kit.Export(Kit.Rich(1, "x", "<p>x</p>"), ExportFormat.Original));
        await Assert.ThrowsAsync<NotSupportedException>(() => Kit.Export(FileNote(1, NoteType.File, "x", "x.bin", "x/y"), ExportFormat.Pdf));
    }

    [Fact]
    public async Task Original_gives_the_bytes_name_and_type()
    {
        var bytes = new byte[] { 1, 2, 3, 250, 0, 7 };
        var (e, _) = With(5, bytes);
        var r = await e.ExportAsync(FileNote(5, NoteType.File, "Titel", "Meine Datei ä.tar.gz", "application/gzip"), ExportFormat.Original, Kit.Plain, Kit.Nothing);
        Assert.Equal(bytes, r.Data);
        Assert.Equal("Meine Datei ä.tar.gz", r.FileName);
        Assert.Equal("application/gzip", r.ContentType);

        var unsafeName = await e.ExportAsync(FileNote(5, NoteType.File, "Titel", "../../evil\\name.exe", ""), ExportFormat.Original, Kit.Plain, Kit.Nothing);
        Assert.Equal("evil name.exe", unsafeName.FileName);
        Assert.Equal("application/octet-stream", unsafeName.ContentType);

        var noName = await e.ExportAsync(FileNote(5, NoteType.Pdf, "Bericht", "", ""), ExportFormat.Original, Kit.Plain, Kit.Nothing);
        Assert.Equal("Bericht.pdf", noName.FileName);
        Assert.Equal("application/pdf", noName.ContentType);
    }

    [Fact]
    public async Task Missing_file_bytes_are_an_error()
    {
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Kit.Export(FileNote(5, NoteType.File, "x", "x.bin", "x/y"), ExportFormat.Original));
    }

    [Fact]
    public async Task Xlsx_to_csv_quotes_and_uses_invariant_values_with_bom()
    {
        var (e, _) = With(7, Sheet());
        var r = await e.ExportAsync(FileNote(7, NoteType.Xlsx, "Zahlen", "z.xlsx", XlsxType), ExportFormat.Csv, Kit.Plain, Kit.Nothing);
        Assert.Equal("text/csv; charset=utf-8", r.ContentType);
        Assert.Equal("Zahlen.csv", r.FileName);
        Assert.Equal([0xEF, 0xBB, 0xBF], r.Data[..3]);
        var csv = new UTF8Encoding(false).GetString(r.Data, 3, r.Data.Length - 3);
        var lines = csv.Split("\r\n");
        Assert.Equal("Name,Betrag,Datum,Notiz", lines[0]);
        Assert.Contains("\"Müller, \"\"Hans\"\"\",1234.5,2026-10-14,\"zwei\nZeilen, ja\"\r\n", csv);
    }

    [Fact]
    public async Task Xlsx_to_csv_is_the_first_sheet_only_and_formulas_have_values()
    {
        var (e, _) = With(7, Sheet());
        var r = await e.ExportAsync(FileNote(7, NoteType.Xlsx, "Zahlen", "z.xlsx", XlsxType), ExportFormat.Csv, Kit.Plain, Kit.Nothing);
        var csv = Encoding.UTF8.GetString(r.Data);
        Assert.DoesNotContain("second sheet", csv);
        Assert.Contains("日本語,0.1,TRUE,2469", csv);
        Assert.Contains("<script>alert(1)</script>", csv); // CSV is data: no HTML handling
        Assert.DoesNotContain(",\r\n\r\n", csv);
    }

    [Fact]
    public async Task Xlsx_to_html_has_a_table_per_sheet_and_encodes_cells()
    {
        var (e, _) = With(7, Sheet());
        var r = await e.ExportAsync(FileNote(7, NoteType.Xlsx, "Zahlen", "z.xlsx", XlsxType), ExportFormat.Html, Kit.Plain, Kit.Nothing);
        var html = Kit.Utf8(r);
        Assert.Contains("<h2>Umsatz ä</h2>", html);
        Assert.Contains("<h2>Zweites</h2>", html);
        Assert.Equal(2, html.Split("<table>").Length - 1);
        Assert.Contains("1,234.50", html); // as Excel shows it
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
    }

    [Fact]
    public async Task Docx_file_to_txt_and_html_extracts_the_content()
    {
        var word = (await Kit.Export(Kit.Rich(1, "Quelle", "<h1>Überschrift</h1><p>Absatz mit <strong>fett</strong>.</p><ul><li>Punkt</li></ul><table><tr><td>a</td><td>b</td></tr></table>"), ExportFormat.Docx)).Data;
        var (e, _) = With(8, word);
        var note = FileNote(8, NoteType.Docx, "Brief", "brief.docx", DocxType);
        var txt = Kit.Utf8(await e.ExportAsync(note, ExportFormat.Txt, Kit.Plain, Kit.Nothing));
        Assert.Contains("Überschrift", txt);
        Assert.Contains("Absatz mit fett.", txt);
        Assert.Contains("- Punkt", txt);
        Assert.Contains("a | b", txt);
        var html = Kit.Utf8(await e.ExportAsync(note, ExportFormat.Html, Kit.Plain, Kit.Nothing));
        Assert.Contains("<h1>Überschrift</h1>", html);
        Assert.Contains("<strong>fett</strong>", html);
        Assert.Contains("<li>Punkt</li>", html);
        Assert.Contains("<table>", html);
    }

    [Fact]
    public async Task Pdf_file_to_txt_extracts_the_text()
    {
        var pdf = (await Kit.Export(Kit.Rich(1, "Quelle", "<p>Hallo PDF äöü</p><p>Zweiter Absatz</p>"), ExportFormat.Pdf)).Data;
        var (e, _) = With(9, pdf);
        var r = await e.ExportAsync(FileNote(9, NoteType.Pdf, "Doku", "doku.pdf", "application/pdf"), ExportFormat.Txt, Kit.Plain, Kit.Nothing);
        var txt = Kit.Utf8(r);
        Assert.Equal("Doku.txt", r.FileName);
        Assert.Contains("Hallo PDF äöü", txt);
        Assert.Contains("Zweiter Absatz", txt);
    }

    [Fact]
    public async Task Export_many_makes_a_zip_with_folders_safe_names_and_unique_paths()
    {
        var store = new Kit.Store();
        store.Files[20] = [9, 9, 9];
        store.Files[21] = Sheet();
        var items = new (string, Note)[]
        {
            ("", Kit.Rich(1, "Alpha", "<p>a</p>")),
            ("Projekte/Q1", Kit.Rich(2, "Alpha", "<p>b</p>")),
            ("Projekte/Q1", Kit.Rich(3, "Alpha", "<p>c</p>")),
            ("Projekte\\..\\Q1", Kit.Md(4, "A/B:C", "# x")),
            ("..", Kit.Txt(5, "Plain ä", "t")),
            ("Dateien", FileNote(20, NoteType.File, "Bin", "data.bin", "application/octet-stream")),
            ("Dateien", FileNote(21, NoteType.Xlsx, "Tabelle", "t.xlsx", XlsxType)),
            ("Dateien", FileNote(22, NoteType.File, "Fehlt", "missing.bin", "x/y")),
        };
        var r = await Kit.Exporter(store).ExportManyAsync("Meine Notizen: 2026", items, ExportFormat.Markdown, Kit.Plain, Kit.Nothing);
        Assert.Equal("application/zip", r.ContentType);
        Assert.Equal("Meine Notizen 2026.zip", r.FileName);
        using var zip = new ZipArchive(new MemoryStream(r.Data));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(
        [
            "Alpha.md",
            "Projekte/Q1/Alpha.md",
            "Projekte/Q1/Alpha (2).md",
            "Projekte/Q1/A B C.md",
            "Plain ä.md",
            "Dateien/data.bin",
            "Dateien/t.xlsx",
        ], names);
        Assert.All(names, n => Assert.DoesNotContain("..", n));
        using var reader = new StreamReader(zip.GetEntry("Projekte/Q1/Alpha (2).md")!.Open());
        Assert.Contains("c", reader.ReadToEnd());
    }

    [Fact]
    public async Task Export_many_uses_the_first_format_when_the_requested_one_does_not_fit()
    {
        var store = new Kit.Store();
        store.Files[21] = Sheet();
        var items = new (string, Note)[] { ("", Kit.Rich(1, "R", "<p>x</p>")), ("", FileNote(21, NoteType.Xlsx, "X", "x.xlsx", XlsxType)) };
        var r = await Kit.Exporter(store).ExportManyAsync("z", items, ExportFormat.Pdf, Kit.Plain, Kit.Nothing);
        using var zip = new ZipArchive(new MemoryStream(r.Data));
        Assert.Equal(["R.pdf", "x.xlsx"], zip.Entries.Select(e => e.FullName).ToArray());
        var csv = await Kit.Exporter(store).ExportManyAsync("z", items, ExportFormat.Csv, Kit.Plain, Kit.Nothing);
        using var zip2 = new ZipArchive(new MemoryStream(csv.Data));
        Assert.Equal(["R.html", "X.csv"], zip2.Entries.Select(e => e.FullName).ToArray());
    }

    [Fact]
    public async Task Export_many_of_nothing_is_an_empty_zip()
    {
        var r = await Kit.Exporter().ExportManyAsync(string.Empty, [], ExportFormat.Txt, Kit.Plain, Kit.Nothing);
        using var zip = new ZipArchive(new MemoryStream(r.Data));
        Assert.Empty(zip.Entries);
        Assert.Equal("notes.zip", r.FileName);
    }

    [Fact]
    public async Task Export_many_zip_entries_hold_valid_files()
    {
        var items = new (string, Note)[] { ("a", Kit.Rich(1, "Doc", "<p>Ärger</p>")), ("b", Kit.Rich(2, "Doc", "<p>x</p>")) };
        var r = await Kit.Exporter().ExportManyAsync("z", items, ExportFormat.Docx, Kit.Plain, Kit.Nothing);
        using var zip = new ZipArchive(new MemoryStream(r.Data));
        using var ms = new MemoryStream();
        await zip.GetEntry("a/Doc.docx")!.Open().CopyToAsync(ms);
        Assert.Empty(Kit.Validate(ms.ToArray()));
        Assert.Contains("Ärger", Kit.DocxText(ms.ToArray()));
    }
}

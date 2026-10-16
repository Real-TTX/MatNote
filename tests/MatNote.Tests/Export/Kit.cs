using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MatNote.Data;
using MatNote.Services.Export;
using UglyToad.PdfPig;

namespace MatNote.Tests.Export;

/// <summary>Helpers of the export tests: notes, a database-free store, the readers of the formats.</summary>
internal static class Kit
{
    public const string Umlauts = "Ärger öffentlich Übermaß äöüß €";
    public const string Cjk = "日本語のテスト 中文";
    public const string Specials = "a & b < c > d \\ e { f } g";

    public static readonly ExportOptions Plain = new(false);
    public static readonly ExportOptions Inline = new(true, 3);

    public sealed class Store : INoteFileStore
    {
        public Dictionary<long, byte[]> Files { get; } = [];

        public Dictionary<long, string[]> Tags { get; } = [];

        public Dictionary<long, List<(string Name, byte[] Data)>> Attachments { get; } = [];

        public Task<IReadOnlyList<(string Name, byte[] Data)>> GetAttachmentsAsync(long noteId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(string Name, byte[] Data)>>(Attachments.GetValueOrDefault(noteId) ?? []);

        public Task<byte[]?> GetFileAsync(long noteId, CancellationToken ct = default) => Task.FromResult(Files.GetValueOrDefault(noteId));

        public Task<IReadOnlyList<string>> GetTagNamesAsync(long noteId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Tags.GetValueOrDefault(noteId) ?? []);
    }

    public static Note Note(long id, NoteType type, string title, string content = "") => new()
    {
        Id = id,
        Type = type,
        Title = title,
        Content = content,
        CreateDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        UpdateDate = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
    };

    public static Note Rich(long id, string title, string html) => Note(id, NoteType.RichText, title, html);

    public static Note Md(long id, string title, string md) => Note(id, NoteType.Markdown, title, md);

    public static Note Txt(long id, string title, string text) => Note(id, NoteType.PlainText, title, text);

    public static string Link(long id, string title) => $"<a class=\"mn-note-link\" data-note-id=\"{id}\" href=\"/Notes/View/{id}\">{title}</a>";

    public static NoteResolver Resolver(params Note[] notes) => id => Task.FromResult(notes.FirstOrDefault(n => n.Id == id));

    public static readonly NoteResolver Nothing = _ => Task.FromResult<Note?>(null);

    public static NoteExporter Exporter(Store? store = null) => new(store ?? new Store());

    public static NotePreview Preview(Store? store = null) => new(store ?? new Store());

    public static string Utf8(ExportResult r) => new UTF8Encoding(false).GetString(r.Data).TrimStart('﻿');

    public static string Squash(string s) => Regex.Replace(s, "\\s+", string.Empty);

    public static Task<ExportResult> Export(Note note, ExportFormat format, ExportOptions? options = null, NoteResolver? resolver = null, Store? store = null) =>
        Exporter(store).ExportAsync(note, format, options ?? Plain, resolver ?? Nothing);

    public static string PdfText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => p.Text));
    }

    public static string DocxText(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }

    public static List<string> Validate(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(doc)
            .Select(e => $"{e.Description} @ {e.Path?.XPath}").ToList();
    }

    public static byte[] Workbook(Action<XLWorkbook> build)
    {
        using var wb = new XLWorkbook();
        build(wb);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        }

        return ms.ToArray();
    }
}

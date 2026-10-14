using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>Turns notes into files. Text notes go through the document model (<see cref="Doc"/>), files are handed out as they are or as text.</summary>
public sealed class NoteExporter(INoteFileStore store) : INoteExporter
{
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const int SheetRows = 100_000;
    private const int SheetColumns = 1_000;

    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly ExportFormat[] Rich = [ExportFormat.Html, ExportFormat.Txt, ExportFormat.Markdown, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json];
    private static readonly ExportFormat[] Plain = [ExportFormat.Txt, ExportFormat.Markdown, ExportFormat.Html, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json];
    private static readonly ExportFormat[] Mark = [ExportFormat.Markdown, ExportFormat.Txt, ExportFormat.Html, ExportFormat.Docx, ExportFormat.Pdf, ExportFormat.Rtf, ExportFormat.Json];

    public IReadOnlyList<ExportFormat> FormatsFor(NoteType type) => type switch
    {
        NoteType.RichText => Rich,
        NoteType.PlainText => Plain,
        NoteType.Markdown => Mark,
        NoteType.Docx => [ExportFormat.Original, ExportFormat.Txt, ExportFormat.Html],
        NoteType.Xlsx => [ExportFormat.Original, ExportFormat.Csv, ExportFormat.Html],
        NoteType.Pdf => [ExportFormat.Original, ExportFormat.Txt],
        _ => [ExportFormat.Original],
    };

    public async Task<ExportResult> ExportAsync(Note note, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!FormatsFor(note.Type).Contains(format))
        {
            throw new NotSupportedException($"A {note.Type} note cannot be exported as {format}.");
        }

        return NoteDocs.IsText(note.Type)
            ? await ExportTextAsync(note, format, options, resolver, ct)
            : await ExportFileAsync(note, format, ct);
    }

    public async Task<ExportResult> ExportManyAsync(string name, IReadOnlyList<(string Folder, Note Note)> items, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>();
            foreach (var (folder, note) in items)
            {
                ct.ThrowIfCancellationRequested();
                var formats = FormatsFor(note.Type);
                var chosen = formats.Contains(format) ? format : formats[0];
                ExportResult result;
                try
                {
                    result = await ExportAsync(note, chosen, options, resolver, ct);
                }
                catch (MissingFileException)
                {
                    continue; // a file note without its bytes: nothing to add
                }

                var entry = zip.CreateEntry(FileNames.UniquePath(folder ?? string.Empty, result.FileName, used), CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(note.UpdateDate == default ? DateTime.UtcNow : note.UpdateDate, DateTimeKind.Utc));
                await using var target = entry.Open();
                await target.WriteAsync(result.Data, ct);
            }
        }

        return new ExportResult(stream.ToArray(), FileNames.WithExtension(string.IsNullOrWhiteSpace(name) ? "notes" : name, ".zip"), "application/zip");
    }

    private async Task<ExportResult> ExportTextAsync(Note note, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct)
    {
        if (format == ExportFormat.Json)
        {
            return await ExportJsonAsync(note, options, resolver, ct);
        }

        var inliner = new LinkInliner(resolver, options, markdownSource: format == ExportFormat.Markdown, note.Id);
        var blocks = await inliner.ProcessAsync(NoteDocs.Blocks(note, format == ExportFormat.Markdown), ct);
        var doc = new Doc(note.Title ?? string.Empty, blocks);
        return Render(doc, format, note.Title);
    }

    private static ExportResult Render(Doc doc, ExportFormat format, string? title) => format switch
    {
        ExportFormat.Txt => Text(DocText.Render(doc), title, ".txt", "text/plain; charset=utf-8"),
        ExportFormat.Markdown => Text(DocMarkdown.Render(doc), title, ".md", "text/markdown; charset=utf-8"),
        ExportFormat.Html => Text(DocHtml.Document(doc), title, ".html", "text/html; charset=utf-8"),
        ExportFormat.Rtf => new ExportResult(Encoding.ASCII.GetBytes(DocRtf.Render(doc)), FileNames.WithExtension(title, ".rtf"), "application/rtf"),
        ExportFormat.Docx => new ExportResult(DocDocx.Render(doc), FileNames.WithExtension(title, ".docx"), DocxType),
        ExportFormat.Pdf => new ExportResult(DocPdf.Render(doc), FileNames.WithExtension(title, ".pdf"), "application/pdf"),
        _ => throw new NotSupportedException($"{format} is not a text format."),
    };

    private static ExportResult Text(string text, string? title, string extension, string contentType) =>
        new(Utf8.GetBytes(text), FileNames.WithExtension(title, extension), contentType);

    private async Task<ExportResult> ExportFileAsync(Note note, ExportFormat format, CancellationToken ct)
    {
        var data = await store.GetFileAsync(note.Id, ct) ?? throw new MissingFileException(note.Id);
        var title = note.Title;
        if (format == ExportFormat.Original)
        {
            var fallback = FileNames.WithExtension(title, Extension(note.Type));
            return new ExportResult(data, FileNames.Safe(note.FileName, fallback), string.IsNullOrWhiteSpace(note.ContentType) ? DefaultContentType(note.Type) : note.ContentType);
        }

        switch (note.Type, format)
        {
            case (NoteType.Docx, ExportFormat.Txt or ExportFormat.Html):
            {
                var blocks = DocxReader.Read(data, int.MaxValue, out _);
                return Render(new Doc(title, blocks, ShowTitle: false), format, title);
            }

            case (NoteType.Xlsx, ExportFormat.Html):
            {
                var sheets = XlsxReader.Read(data, SheetRows, SheetColumns, display: true);
                return Render(new Doc(title, XlsxReader.ToBlocks(sheets, SheetRows, SheetColumns), ShowTitle: false), format, title);
            }

            case (NoteType.Xlsx, ExportFormat.Csv):
            {
                var sheet = XlsxReader.Read(data, SheetRows, SheetColumns, display: false, firstOnly: true).FirstOrDefault() ?? new SheetData(string.Empty, [], false);
                var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8.GetBytes(XlsxReader.ToCsv(sheet))).ToArray();
                return new ExportResult(bytes, FileNames.WithExtension(title, ".csv"), "text/csv; charset=utf-8");
            }

            case (NoteType.Pdf, ExportFormat.Txt):
                return Text(PdfText.Extract(data, int.MaxValue).Replace("\r\n", "\n") + "\n", title, ".txt", "text/plain; charset=utf-8");
            default:
                throw new NotSupportedException($"A {note.Type} note cannot be exported as {format}.");
        }
    }

    private async Task<ExportResult> ExportJsonAsync(Note note, ExportOptions options, NoteResolver resolver, CancellationToken ct)
    {
        var linked = new List<object>();
        if (options.IncludeLinkedNotes)
        {
            var visited = new HashSet<long> { note.Id };
            await Collect(note, 1);

            async Task Collect(Note from, int depth)
            {
                if (depth > options.MaxDepth)
                {
                    return;
                }

                foreach (var id in NoteDocs.Links(from))
                {
                    if (visited.Contains(id) || await resolver(id) is not { } found || !NoteDocs.IsText(found.Type) || !visited.Add(found.Id))
                    {
                        continue;
                    }

                    linked.Add(new { id = found.Id, title = found.Title, type = found.Type.ToString(), content = found.Content });
                    await Collect(found, depth + 1);
                }
            }
        }

        var tags = await store.GetTagNamesAsync(note.Id, ct);
        var payload = new Dictionary<string, object?>
        {
            ["title"] = note.Title,
            ["type"] = note.Type.ToString(),
            ["tags"] = tags,
            ["created"] = Utc(note.CreateDate),
            ["updated"] = Utc(note.UpdateDate),
            ["content"] = note.Content,
        };
        if (linked.Count > 0)
        {
            payload["linkedNotes"] = linked;
        }

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return Text(json + "\n", note.Title, ".json", "application/json; charset=utf-8");
    }

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static string Extension(NoteType type) => type switch
    {
        NoteType.Docx => ".docx",
        NoteType.Xlsx => ".xlsx",
        NoteType.Pdf => ".pdf",
        _ => ".bin",
    };

    private static string DefaultContentType(NoteType type) => type switch
    {
        NoteType.Docx => DocxType,
        NoteType.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        NoteType.Pdf => "application/pdf",
        _ => "application/octet-stream",
    };

    private sealed class MissingFileException(long noteId) : InvalidOperationException($"The file of note {noteId} is missing.");
}

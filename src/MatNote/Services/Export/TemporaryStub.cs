using System.Text.RegularExpressions;
using MatNote.Data;

namespace MatNote.Services.Export;

// TEMPORARY: stands in for the real exporter until the export subsystem is merged. Delete this file then.
public sealed partial class TemporaryStub : INoteExporter, INotePreview
{
    public IReadOnlyList<ExportFormat> FormatsFor(NoteType type) => Ui.IsText(type) ? [ExportFormat.Txt] : [ExportFormat.Original];

    public Task<ExportResult> ExportAsync(Note note, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default)
        => Task.FromResult(new ExportResult(System.Text.Encoding.UTF8.GetBytes(note.Content), note.Title + ".txt", "text/plain"));

    public Task<ExportResult> ExportManyAsync(string name, IReadOnlyList<(string Folder, Note Note)> items, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default)
        => Task.FromResult(new ExportResult([], name + ".zip", "application/zip"));

    public string RenderText(Note note, Func<long, string> noteUrl) => note.Type == NoteType.RichText ? note.Content : "<pre class=\"mn-plain\">" + System.Net.WebUtility.HtmlEncode(note.Content) + "</pre>";

    public Task<string?> RenderFileAsync(Note note, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<string> BuildSearchTextAsync(Note note, byte[]? file, CancellationToken ct = default) => Task.FromResult(Regex.Replace(note.Content, "<[^>]+>", " "));

    public string Sanitize(string html) => html;

    public IReadOnlyCollection<long> ExtractLinks(Note note) => [];
}

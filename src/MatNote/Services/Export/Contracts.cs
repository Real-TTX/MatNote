using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>The formats a note can be exported in. Which of them a note offers depends on its type (<see cref="INoteExporter.FormatsFor"/>).</summary>
public enum ExportFormat
{
    /// <summary>The file as it was uploaded (Word, Excel, PDF, other files).</summary>
    Original,

    Txt,

    Markdown,

    Html,

    /// <summary>Rich Text Format.</summary>
    Rtf,

    Docx,

    Pdf,

    /// <summary>The first sheet of a workbook, or the lines of a text.</summary>
    Csv,

    /// <summary>The note with its data (title, type, tags, content) as JSON.</summary>
    Json,
}

/// <param name="IncludeLinkedNotes">
/// A note can link to other notes. When this is on, every link is replaced by the content of the linked note (as a titled section where the link was),
/// in the format of the export; a note that is already part of the export is not included a second time (no endless loops).
/// </param>
/// <param name="MaxDepth">How many levels of links are followed (a linked note that links on).</param>
public sealed record ExportOptions(bool IncludeLinkedNotes = true, int MaxDepth = 3);

public sealed record ExportResult(byte[] Data, string FileName, string ContentType);

/// <summary>
/// Gives the note with this id, with its content, or <c>null</c> when the person who exports may not read it (or it does not exist).
/// The exporter calls it for every linked note: THE EXPORT NEVER COPIES A NOTE THE PERSON CANNOT SEE.
/// </summary>
public delegate Task<Note?> NoteResolver(long noteId);

/// <summary>Turns notes into files in the formats the user asks for.</summary>
public interface INoteExporter
{
    /// <summary>The formats that make sense for this type of note, the best first.</summary>
    IReadOnlyList<ExportFormat> FormatsFor(NoteType type);

    Task<ExportResult> ExportAsync(Note note, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default);

    /// <summary>A zip of many notes: <paramref name="items"/> are the notes with their path inside the zip (folders as directories). A file note is added as the file.</summary>
    Task<ExportResult> ExportManyAsync(string name, IReadOnlyList<(string Folder, Note Note)> items, ExportFormat format, ExportOptions options, NoteResolver resolver, CancellationToken ct = default);
}

/// <summary>Showing a note on a page, and what the search and the links need to know about it.</summary>
public interface INotePreview
{
    /// <summary>
    /// The HTML of a text note for the page: a rich text note sanitized, markdown rendered and sanitized, plain text encoded in a block that keeps its line breaks.
    /// <paramref name="noteUrl"/> gives the address of a linked note for the page it is shown on (<c>/Notes/View/5</c>, or inside a shared link).
    /// </summary>
    string RenderText(Note note, Func<long, string> noteUrl);

    /// <summary>
    /// The text of a note for the page with the linked notes in it, exactly as the export puts them: the note stays, and each linked note follows as a titled section right after
    /// the paragraph (the list item, the table, the line of a plain text) that links it. Only notes the <paramref name="resolver"/> gives, once each, three levels deep.
    /// The title of a section is a link to its note.
    /// </summary>
    Task<string> RenderWithLinkedAsync(Note note, NoteResolver resolver, Func<long, string> noteUrl, CancellationToken ct = default);

    /// <summary>A preview of a Word or Excel file as safe HTML (a limited number of rows and columns), or <c>null</c> for any other type.</summary>
    Task<string?> RenderFileAsync(Note note, CancellationToken ct = default);

    /// <summary>The text of the note without markup, or the text found in the file (Word, Excel, PDF): what the search looks in. Never more than 200,000 characters.</summary>
    Task<string> BuildSearchTextAsync(Note note, byte[]? file, CancellationToken ct = default);

    /// <summary>The rich text HTML the editor posts, cleaned: no script, no event handler, no style, no foreign frames; the links to notes are kept.</summary>
    string Sanitize(string html);

    /// <summary>The ids of the notes this note links to (rich text: links with <c>data-note-id</c>, markdown and plain text: <c>[Title](note:ID)</c>).</summary>
    IReadOnlyCollection<long> ExtractLinks(Note note);
}

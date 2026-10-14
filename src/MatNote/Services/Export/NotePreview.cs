using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>Safe HTML for the page, the text the search looks in, and the links of a note.</summary>
public sealed partial class NotePreview(INoteFileStore store) : INotePreview
{
    private const int SearchCap = 200_000;
    private const int PreviewRows = 500;
    private const int PreviewColumns = 50;
    private const int PreviewChars = 300_000;

    private static readonly HashSet<string> InlineTags = new(StringComparer.Ordinal)
    {
        "a", "b", "strong", "i", "em", "u", "s", "strike", "del", "span", "code", "sub", "sup", "mark", "small", "font", "abbr", "cite", "q", "kbd", "samp", "var", "ins", "tt", "big",
    };

    private static readonly HashSet<string> SkippedTags = new(StringComparer.Ordinal) { "script", "style", "head", "template", "noscript", "svg", "math" };

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    public string Sanitize(string html) => NoteDocs.Sanitizer.Sanitize(html);

    public string RenderText(Note note, Func<long, string> noteUrl)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(noteUrl);
        return note.Type switch
        {
            NoteType.RichText => RewriteLinks(Sanitize(note.Content), noteUrl),
            NoteType.Markdown => RewriteLinks(MarkdownService.ToHtml(note.Content, NoteDocs.Sanitizer), noteUrl),
            NoteType.PlainText => PlainHtml(note.Content, noteUrl),
            _ => string.Empty,
        };
    }

    /// <summary>The address of a note for the page; anything that is not an address of this site or a web address is refused (no script URLs).</summary>
    private static string? SafeUrl(Func<long, string> noteUrl, long id)
    {
        var url = noteUrl(id)?.Trim();
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl))
        {
            return null;
        }

        var ok = (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal))
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        return ok ? url : null;
    }

    private static string RewriteLinks(string sanitized, Func<long, string> noteUrl)
    {
        if (!sanitized.Contains("data-note-id", StringComparison.Ordinal))
        {
            return sanitized;
        }

        var document = new HtmlParser().ParseDocument("<body>" + sanitized);
        foreach (var a in document.QuerySelectorAll("a[data-note-id]"))
        {
            var noteId = a.GetAttribute("data-note-id") ?? string.Empty;
            var url = long.TryParse(noteId, out var id) ? SafeUrl(noteUrl, id) : null;

            // one fixed order of the attributes, whatever the author or the markdown renderer wrote
            foreach (var name in new[] { "class", "data-note-id", "href", "rel", "target" })
            {
                a.RemoveAttribute(name);
            }

            a.SetAttribute("class", NoteSanitizer.NoteLinkClass);
            a.SetAttribute("data-note-id", noteId);
            if (url is not null)
            {
                a.SetAttribute("href", url);
            }
        }

        return document.Body!.InnerHtml;
    }

    private static string PlainHtml(string? text, Func<long, string> noteUrl)
    {
        text = Inlines.Strip(text ?? string.Empty);
        var sb = new StringBuilder("<pre class=\"mn-plain\">");
        var last = 0;
        foreach (Match m in MarkdownService.NoteToken().Matches(text))
        {
            sb.Append(DocHtml.Enc(text[last..m.Index]));
            var id = long.Parse(m.Groups[2].Value);
            var title = m.Groups[1].Value.Trim();
            sb.Append("<a class=\"mn-note-link\" data-note-id=\"").Append(id).Append('"');
            if (SafeUrl(noteUrl, id) is { } url)
            {
                sb.Append(" href=\"").Append(DocHtml.Enc(url)).Append('"');
            }

            sb.Append('>').Append(DocHtml.Enc(title.Length == 0 ? $"Note {id}" : title)).Append("</a>");
            last = m.Index + m.Length;
        }

        sb.Append(DocHtml.Enc(text[last..])).Append("</pre>");
        return sb.ToString();
    }

    public async Task<string?> RenderFileAsync(Note note, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Type is not (NoteType.Docx or NoteType.Xlsx))
        {
            return null;
        }

        try
        {
            var data = await store.GetFileAsync(note.Id, ct);
            if (data is null || data.Length == 0)
            {
                return Message("This file has no content.");
            }

            ct.ThrowIfCancellationRequested();
            string html;
            if (note.Type == NoteType.Docx)
            {
                var blocks = DocxReader.Read(data, PreviewChars, out var truncated);
                html = DocHtml.Fragment(blocks);
                if (truncated)
                {
                    html += "<p><em>The preview is shortened: download the file to read all of it.</em></p>\n";
                }
            }
            else
            {
                var sheets = XlsxReader.Read(data, PreviewRows, PreviewColumns, display: true);
                html = DocHtml.Fragment(XlsxReader.ToBlocks(sheets, PreviewRows, PreviewColumns));
            }

            return html.Length == 0 ? Message("This file has no content.") : html;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Message("The preview could not be created: the file is damaged or not supported. Download it to open it.");
        }
    }

    private static string Message(string text) => "<p><em>" + DocHtml.Enc(text) + "</em></p>";

    public async Task<string> BuildSearchTextAsync(Note note, byte[]? file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            string text;
            switch (note.Type)
            {
                case NoteType.RichText:
                    text = HtmlText(note.Content);
                    break;
                case NoteType.PlainText or NoteType.Markdown:
                    text = note.Content ?? string.Empty;
                    break;
                case NoteType.Docx or NoteType.Xlsx or NoteType.Pdf:
                    file ??= note.Id > 0 ? await store.GetFileAsync(note.Id, ct) : null;
                    text = file is null || file.Length == 0 ? string.Empty : FileText(note.Type, file);
                    break;
                default:
                    return string.Empty;
            }

            text = Whitespace().Replace(Inlines.Strip(text), " ").Trim();
            return text.Length > SearchCap ? text[..SearchCap].TrimEnd() : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty; // a damaged file is simply not searchable
        }
    }

    private static string FileText(NoteType type, byte[] data)
    {
        switch (type)
        {
            case NoteType.Docx:
            {
                var blocks = DocxReader.Read(data, SearchCap * 2, out _);
                return DocText.Flat(blocks);
            }

            case NoteType.Xlsx:
            {
                var sb = new StringBuilder();
                foreach (var sheet in XlsxReader.Read(data, 20_000, 200, display: true))
                {
                    sb.Append(sheet.Name).Append(' ');
                    foreach (var row in sheet.Rows)
                    {
                        foreach (var cell in row.Where(c => c.Length > 0))
                        {
                            sb.Append(cell).Append(' ');
                        }

                        if (sb.Length > SearchCap * 2)
                        {
                            return sb.ToString();
                        }
                    }
                }

                return sb.ToString();
            }

            default:
                return PdfText.Extract(data, SearchCap * 2);
        }
    }

    private static string HtmlText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlParser().ParseDocument(html);
        var sb = new StringBuilder();

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                switch (child)
                {
                    case IText t:
                        sb.Append(t.Data);
                        break;
                    case IElement e when SkippedTags.Contains(e.LocalName):
                        break;
                    case IElement e:
                        var block = !InlineTags.Contains(e.LocalName);
                        if (block)
                        {
                            sb.Append(' ');
                        }

                        Walk(e);
                        if (block)
                        {
                            sb.Append(' ');
                        }

                        break;
                }
            }
        }

        if (document.Body is { } body)
        {
            Walk(body);
        }

        return sb.ToString();
    }

    public IReadOnlyCollection<long> ExtractLinks(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return NoteDocs.Links(note);
    }
}

using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>Reads the content of a text note into the document model, and finds the links of a note.</summary>
internal static class NoteDocs
{
    public static readonly NoteSanitizer Sanitizer = new();

    public static bool IsText(NoteType type) => type is NoteType.RichText or NoteType.PlainText or NoteType.Markdown;

    /// <param name="markdownSource">Keep a markdown note as markdown source (for the markdown export) instead of reading it through HTML.</param>
    public static List<Block> Blocks(Note note, bool markdownSource = false) => note.Type switch
    {
        NoteType.RichText => HtmlToDoc.Parse(note.Content),
        NoteType.Markdown when markdownSource => MarkdownChunks(note.Content),
        NoteType.Markdown => HtmlToDoc.Parse(MarkdownService.ToHtml(note.Content, Sanitizer)),
        NoteType.PlainText => PlainBlocks(note.Content),
        _ => [],
    };

    private static string Normalize(string? text) => Inlines.Strip((text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n'));

    /// <summary>Plain text: paragraphs separated by blank lines, line breaks kept, <c>[Title](note:42)</c> as a link to the note.</summary>
    public static List<Block> PlainBlocks(string? text)
    {
        var blocks = new List<Block>();
        var current = new List<Inline>();

        void Flush()
        {
            var inlines = Inlines.Normalize(current, collapse: false);
            current = [];
            if (inlines.Count > 0)
            {
                blocks.Add(new Para(inlines, LineMode: true));
            }
        }

        foreach (var line in Normalize(text).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            if (current.Count > 0)
            {
                current.Add(Inline.Break);
            }

            var last = 0;
            foreach (Match m in MarkdownService.NoteToken().Matches(line))
            {
                if (m.Index > last)
                {
                    current.Add(new Inline(line[last..m.Index]));
                }

                var id = long.Parse(m.Groups[2].Value);
                var title = m.Groups[1].Value.Trim();
                current.Add(new Inline(title.Length == 0 ? $"Note {id}" : title, NoteId: id));
                last = m.Index + m.Length;
            }

            if (last < line.Length)
            {
                current.Add(new Inline(line[last..]));
            }
        }

        Flush();
        return blocks;
    }

    /// <summary>Markdown as source paragraphs (a fenced code block is one chunk), so that a linked note can be put after the paragraph with the link.</summary>
    public static List<Block> MarkdownChunks(string? markdown)
    {
        var blocks = new List<Block>();
        var current = new List<string>();
        string? fence = null;
        var fenceLines = new List<string>();

        void Flush()
        {
            if (current.Count > 0)
            {
                blocks.Add(new MdSource(string.Join('\n', current), false));
                current = [];
            }
        }

        foreach (var line in Normalize(markdown).Split('\n'))
        {
            var trimmed = line.TrimStart(' ');
            var indent = line.Length - trimmed.Length;
            if (fence is not null)
            {
                fenceLines.Add(line);
                if (indent < 4 && trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.TrimStart(fence[0]).Trim().Length == 0)
                {
                    blocks.Add(new MdSource(string.Join('\n', fenceLines), true));
                    fence = null;
                    fenceLines = [];
                }

                continue;
            }

            if (indent < 4 && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                Flush();
                var ch = trimmed[0];
                fence = new string(ch, trimmed.TakeWhile(c => c == ch).Count());
                fenceLines = [line];
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            current.Add(line);
        }

        Flush();
        if (fenceLines.Count > 0)
        {
            blocks.Add(new MdSource(string.Join('\n', fenceLines), true)); // an unclosed fence runs to the end
        }

        return blocks;
    }

    /// <summary>The distinct ids of the notes this note links to, in the order of the content.</summary>
    public static List<long> Links(Note note)
    {
        var ids = new List<long>();
        var seen = new HashSet<long>();
        void Add(string? s)
        {
            if (!string.IsNullOrEmpty(s) && s.Length <= 18 && s.All(char.IsAsciiDigit) && long.TryParse(s, out var id) && id > 0 && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        switch (note.Type)
        {
            case NoteType.RichText:
                if (note.Content.Contains("data-note-id", StringComparison.OrdinalIgnoreCase))
                {
                    var document = new HtmlParser().ParseDocument(note.Content);
                    foreach (var a in document.QuerySelectorAll("a[data-note-id]"))
                    {
                        Add(a.GetAttribute("data-note-id"));
                    }
                }

                break;
            case NoteType.Markdown or NoteType.PlainText:
                foreach (Match m in MarkdownService.NoteToken().Matches(note.Content))
                {
                    Add(m.Groups[2].Value);
                }

                break;
        }

        return ids;
    }
}

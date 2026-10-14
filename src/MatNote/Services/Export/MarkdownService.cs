using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MatNote.Services.Export;

/// <summary>Markdown to (sanitized) HTML. A link <c>[Title](note:42)</c> becomes an anchor with class <c>mn-note-link</c> and <c>data-note-id</c>, like the editor makes them.</summary>
internal static partial class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UseTaskLists()
        .Build();

    /// <summary>The token of a link to a note in markdown and plain text: <c>[Title](note:42)</c>.</summary>
    [GeneratedRegex("\\[([^\\]\\r\\n]*)\\]\\(note:(\\d{1,18})\\)")]
    public static partial Regex NoteToken();

    public static string ToHtml(string markdown, NoteSanitizer sanitizer)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (link.IsImage || link.Url is null)
            {
                continue;
            }

            var m = NoteUrl().Match(link.Url.Trim());
            if (!m.Success)
            {
                continue;
            }

            link.Url = "/Notes/View/" + m.Groups[1].Value;
            var attributes = link.GetAttributes();
            attributes.AddClass(NoteSanitizer.NoteLinkClass);
            attributes.AddProperty("data-note-id", m.Groups[1].Value);
        }

        var html = document.ToHtml(Pipeline);
        return sanitizer.Sanitize(html);
    }

    [GeneratedRegex("^note:(\\d{1,18})$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteUrl();
}

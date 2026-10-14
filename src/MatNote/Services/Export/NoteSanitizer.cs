using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;

namespace MatNote.Services.Export;

/// <summary>The HTML sanitizer for rich text: the editor's subset only, no style, no event handler, no script, only safe addresses.</summary>
internal sealed partial class NoteSanitizer
{
    public const string NoteLinkClass = "mn-note-link";

    private static readonly string[] Tags =
    [
        "p", "br", "div", "span", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "strike", "del", "ul", "ol", "li", "blockquote", "pre", "code", "a", "hr",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td",
    ];

    [ThreadStatic]
    private static HtmlSanitizer? cached;

    /// <summary>The address of a link that may stay: http(s), mailto, and the app's own <c>/Notes/View/…</c> and <c>/s/…</c> (no protocol-relative address).</summary>
    [GeneratedRegex("^(?:https?://[^\\s]+|mailto:[^\\s]+|/Notes/View/[^\\s/]+|/s/[^\\s]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeHref();

    [GeneratedRegex("^(?:https?://|mailto:)", RegexOptions.IgnoreCase)]
    private static partial Regex External();

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return (cached ??= Create()).Sanitize(html);
    }

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        s.AllowedTags.UnionWith(Tags);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.UnionWith(["href", "class", "data-note-id", "rel"]);
        s.AllowDataAttributes = false;
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.AllowedClasses.Clear();
        s.AllowedClasses.Add(NoteLinkClass);
        s.KeepChildNodes = false;
        s.PostProcessNode += (_, e) => Post(e.Node);
        return s;
    }

    private static void Post(INode node)
    {
        if (node is not IElement el)
        {
            return;
        }

        el.RemoveAttribute("style");
        el.RemoveAttribute("target");
        var id = el.GetAttribute("data-note-id");
        if (id is not null && !(id.Length is > 0 and <= 18 && id.All(char.IsAsciiDigit)))
        {
            el.RemoveAttribute("data-note-id");
        }

        if (el.LocalName != "a")
        {
            el.RemoveAttribute("href");
            el.RemoveAttribute("rel");
            return;
        }

        var href = el.GetAttribute("href");
        if (href is not null && !SafeHref().IsMatch(href))
        {
            el.RemoveAttribute("href");
            href = null;
        }

        if (href is not null && External().IsMatch(href))
        {
            el.SetAttribute("rel", "noopener noreferrer");
        }
        else
        {
            el.RemoveAttribute("rel");
        }
    }
}

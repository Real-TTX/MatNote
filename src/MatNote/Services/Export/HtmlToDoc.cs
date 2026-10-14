using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace MatNote.Services.Export;

/// <summary>Reads the HTML of the editor (or of rendered markdown) into the document model. Only structure and text are taken: nothing active can get through.</summary>
internal static partial class HtmlToDoc
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "template", "noscript", "iframe", "object", "embed", "svg", "math", "title", "textarea", "select", "button", "audio", "video", "canvas", "form", "input",
    };

    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "figure", "figcaption", "address", "dl", "dt", "dd", "center", "li", "details", "summary", "body", "html",
    };

    public static List<Block> Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html ?? string.Empty);
        var ctx = new Ctx();
        if (document.Body is { } body)
        {
            Walk(body, new Fmt(), ctx);
        }

        ctx.Flush();
        return ctx.Blocks;
    }

    private sealed record Fmt(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, bool Code = false, string? Href = null, long? NoteId = null);

    private sealed class Ctx
    {
        public List<Block> Blocks { get; } = [];

        public List<Inline> Pending { get; } = [];

        public void Flush()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            var inlines = Inlines.Normalize([.. Pending]);
            Pending.Clear();
            if (inlines.Count > 0)
            {
                Blocks.Add(new Para(inlines));
            }
        }
    }

    private static void Walk(INode node, Fmt fmt, Ctx ctx)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    ctx.Pending.Add(Make(text.Data, fmt));
                    break;
                case IElement el:
                    Element(el, fmt, ctx);
                    break;
            }
        }
    }

    private static Inline Make(string text, Fmt f) => new(text, f.Bold, f.Italic, f.Underline, f.Strike, f.Code, f.Href, f.NoteId);

    private static void Element(IElement el, Fmt fmt, Ctx ctx)
    {
        var tag = el.LocalName;
        if (Skipped.Contains(tag))
        {
            return;
        }

        switch (tag)
        {
            case "br":
                ctx.Pending.Add(Inline.Break);
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                ctx.Flush();
                var sub = new Ctx();
                Walk(el, fmt with { Bold = false }, sub);
                var inlines = Inlines.Normalize([.. sub.Pending, .. sub.Blocks.SelectMany(Inlines.Of)]);
                if (inlines.Count > 0)
                {
                    ctx.Blocks.Add(new Heading(Math.Min(tag[1] - '0', 4), inlines));
                }

                return;
            }

            case "ul" or "ol":
            {
                ctx.Flush();
                var items = new List<List<Block>>();
                foreach (var li in el.Children)
                {
                    if (li.LocalName != "li")
                    {
                        continue;
                    }

                    var sub = new Ctx();
                    Walk(li, fmt, sub);
                    sub.Flush();
                    items.Add(sub.Blocks);
                }

                if (items.Count > 0)
                {
                    var start = 1;
                    if (tag == "ol" && int.TryParse(el.GetAttribute("start"), out var s) && s is > 0 and < 100000)
                    {
                        start = s;
                    }

                    ctx.Blocks.Add(new ListBlock(tag == "ol", items, start));
                }

                return;
            }

            case "blockquote":
            {
                ctx.Flush();
                var sub = new Ctx();
                Walk(el, fmt, sub);
                sub.Flush();
                if (sub.Blocks.Count > 0)
                {
                    ctx.Blocks.Add(new Quote(sub.Blocks));
                }

                return;
            }

            case "pre":
            {
                ctx.Flush();
                var text = Inlines.Strip(PreText(el)).Replace("\r\n", "\n").Replace('\r', '\n');
                text = text.TrimStart('\n').TrimEnd('\n', ' ');
                if (text.Length > 0)
                {
                    ctx.Blocks.Add(new CodeBlock(text));
                }

                return;
            }

            case "hr":
                ctx.Flush();
                ctx.Blocks.Add(new Rule());
                return;
            case "table":
                ctx.Flush();
                if (ReadTable(el, fmt) is { } table)
                {
                    ctx.Blocks.Add(table);
                }

                return;
        }

        if (Containers.Contains(tag))
        {
            ctx.Flush();
            Walk(el, fmt, ctx);
            ctx.Flush();
            return;
        }

        var f = fmt;
        switch (tag)
        {
            case "strong" or "b":
                f = f with { Bold = true };
                break;
            case "em" or "i" or "cite" or "dfn" or "var":
                f = f with { Italic = true };
                break;
            case "u" or "ins":
                f = f with { Underline = true };
                break;
            case "s" or "strike" or "del":
                f = f with { Strike = true };
                break;
            case "code" or "kbd" or "samp" or "tt":
                f = f with { Code = true };
                break;
            case "a":
                f = f with { Href = el.GetAttribute("href"), NoteId = ParseId(el.GetAttribute("data-note-id")) };
                break;
        }

        f = ApplyStyle(el.GetAttribute("style"), f);
        Walk(el, f, ctx);
    }

    private static long? ParseId(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 18 && value.All(char.IsAsciiDigit) && long.TryParse(value, out var id) && id > 0 ? id : null;

    [GeneratedRegex("font-weight\\s*:\\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)]
    private static partial Regex BoldStyle();

    [GeneratedRegex("font-style\\s*:\\s*italic", RegexOptions.IgnoreCase)]
    private static partial Regex ItalicStyle();

    [GeneratedRegex("text-decoration[a-z-]*\\s*:[^;]*underline", RegexOptions.IgnoreCase)]
    private static partial Regex UnderlineStyle();

    [GeneratedRegex("text-decoration[a-z-]*\\s*:[^;]*line-through", RegexOptions.IgnoreCase)]
    private static partial Regex StrikeStyle();

    private static Fmt ApplyStyle(string? style, Fmt f)
    {
        if (string.IsNullOrEmpty(style))
        {
            return f;
        }

        return f with
        {
            Bold = f.Bold || BoldStyle().IsMatch(style),
            Italic = f.Italic || ItalicStyle().IsMatch(style),
            Underline = f.Underline || UnderlineStyle().IsMatch(style),
            Strike = f.Strike || StrikeStyle().IsMatch(style),
        };
    }

    private static string PreText(INode node)
    {
        var sb = new System.Text.StringBuilder();
        void Collect(INode n)
        {
            foreach (var c in n.ChildNodes)
            {
                switch (c)
                {
                    case IText t:
                        sb.Append(t.Data);
                        break;
                    case IElement { LocalName: "br" }:
                        sb.Append('\n');
                        break;
                    case IElement e when !Skipped.Contains(e.LocalName):
                        Collect(e);
                        break;
                }
            }
        }

        Collect(node);
        return sb.ToString();
    }

    private static TableBlock? ReadTable(IElement table, Fmt fmt)
    {
        var rows = new List<List<Cell>>();
        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            // nested tables are read as part of their cell text, not as rows of this table
            if (tr.Closest("table") != table)
            {
                continue;
            }

            var cells = new List<Cell>();
            foreach (var td in tr.Children)
            {
                if (td.LocalName is not ("td" or "th"))
                {
                    continue;
                }

                var sub = new Ctx();
                Walk(td, fmt, sub);
                sub.Flush();
                var inlines = new List<Inline>();
                foreach (var b in sub.Blocks)
                {
                    if (inlines.Count > 0)
                    {
                        inlines.Add(Inline.Break);
                    }

                    inlines.AddRange(Flatten(b));
                }

                cells.Add(new Cell(td.LocalName == "th", Inlines.Normalize(inlines)));
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var width = rows.Max(r => r.Count);
        foreach (var r in rows)
        {
            while (r.Count < width)
            {
                r.Add(new Cell(false, []));
            }
        }

        return new TableBlock(rows);
    }

    /// <summary>The text of a block inside a table cell as inlines (a cell holds text only).</summary>
    internal static IEnumerable<Inline> Flatten(Block block)
    {
        switch (block)
        {
            case Para p:
                return p.Inlines;
            case Heading h:
                return h.Inlines.Select(i => i with { Bold = true });
            case CodeBlock c:
                return [new Inline(c.Text, Code: true)];
            case ListBlock l:
                var list = new List<Inline>();
                var n = l.Start;
                foreach (var item in l.Items)
                {
                    if (list.Count > 0)
                    {
                        list.Add(Inline.Break);
                    }

                    list.Add(new Inline(l.Ordered ? $"{n++}. " : "• "));
                    var firstBlock = true;
                    foreach (var b in item)
                    {
                        if (!firstBlock)
                        {
                            list.Add(Inline.Break);
                        }

                        firstBlock = false;
                        list.AddRange(Flatten(b));
                    }
                }

                return list;
            case Quote q:
                return q.Blocks.SelectMany((b, i) => i == 0 ? Flatten(b) : Flatten(b).Prepend(Inline.Break));
            default:
                return [];
        }
    }
}
